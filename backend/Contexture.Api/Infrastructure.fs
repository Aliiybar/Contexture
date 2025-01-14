namespace Contexture.Api.Infrastructure

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Runtime.CompilerServices
open System.Threading.Tasks

 module Async =

    let map mapper o =
        async {
            let! result = o
            return mapper result
        }

    let bindOption o =
        async {
            match o with
            | Some value ->
                let! v = value
                return Some v
            | None -> return None
        }

    let optionMap mapper o =
        async {
            let! bound = bindOption o
            return Option.map mapper bound
        }

type Agent<'T> = MailboxProcessor<'T>

type Clock = unit -> System.DateTime

type EventSource = System.Guid
type StreamKind =
    private
    | SystemType of System.Type
    static member Of<'E>() = SystemType typeof<'E>
type EventMetadata =
    { Source: EventSource
      RecordedAt: System.DateTime }

type EventEnvelope<'Event> =
    { Metadata: EventMetadata
      Event: 'Event }

type EventEnvelope =
    { Metadata: EventMetadata
      Payload: obj
      EventType: System.Type
      StreamKind: StreamKind }

type Subscription<'E> = EventEnvelope<'E> list -> Async<unit>

type private SubscriptionWrapper = EventEnvelope list -> Async<unit>

module EventEnvelope =
    let box (envelope: EventEnvelope<'E>) =
        { Metadata = envelope.Metadata
          Payload = box envelope.Event
          EventType = typeof<'E>
          StreamKind = StreamKind.Of<'E>() }

    let unbox (envelope: EventEnvelope) : EventEnvelope<'E> =
        { Metadata = envelope.Metadata
          Event = unbox<'E> envelope.Payload }

type EventResult = Result<EventEnvelope list, string>

module Storage =

    type StreamIdentifier = EventSource * StreamKind

    type EventStorage =
        abstract member Stream : StreamIdentifier -> Async<EventResult>
        abstract member AllStreamsOf : StreamKind -> Async<EventResult>
        abstract member Append : EventEnvelope list -> Async<unit>
        abstract member All : unit -> Async<EventResult>

    module InMemoryStorage =

        type Msg =
            private
            | Get of StreamKind * AsyncReplyChannel<EventResult>
            | GetStream of StreamIdentifier * AsyncReplyChannel<EventResult>
            | GetAll of AsyncReplyChannel<EventResult>
            | Append of EventEnvelope list * AsyncReplyChannel<unit>

        type private History =
            { items: EventEnvelope list
              byIdentifier: Dictionary<StreamIdentifier, EventEnvelope list>
              byEventType: Dictionary<StreamKind, EventEnvelope list> }
            static member Empty =
                { items = []
                  byIdentifier = Dictionary()
                  byEventType = Dictionary() }

        let private stream history source =
            let (success, events) = history.byIdentifier.TryGetValue source
            if success then events else []

        let private getAllStreamsOf history key : EventEnvelope list =
            let (success, items) = history.byEventType.TryGetValue key
            if success then items else []

        let private appendToHistory (history: History) (envelope: EventEnvelope) =
            let source = envelope.Metadata.Source
            let streamKind = envelope.StreamKind
            let key = (source, streamKind)

            let fullStream =
                key |> stream history |> fun s -> s @ [ envelope ]

            history.byIdentifier.[key] <- fullStream
            let allEvents = getAllStreamsOf history streamKind
            history.byEventType.[streamKind] <- allEvents @ [ envelope ]
            
            { history with items = envelope :: history.items }

        let initialize (initialEvents: EventEnvelope list) =
            let proc (inbox: Agent<Msg>) =
                let rec loop history =
                    async {
                        let! msg = inbox.Receive()

                        match msg with
                        | Get (kind, reply) ->
                            kind
                            |> getAllStreamsOf history
                            |> Ok
                            |> reply.Reply

                            return! loop history

                        | GetStream (identifier, reply) ->
                            identifier |> stream history |> Ok |> reply.Reply

                            return! loop history
                            
                        | GetAll reply ->
                            history.items |> Ok |> reply.Reply
                            
                            return! loop history

                        | Append (events, reply) ->
                            reply.Reply()

                            let extendedHistory =
                                events |> List.fold appendToHistory history

                            return! loop extendedHistory
                    }

                let initialHistory =
                    initialEvents
                    |> List.fold appendToHistory History.Empty

                loop initialHistory

            let agent = Agent<Msg>.Start (proc)

            { new EventStorage with
                member _.Stream identifier =
                    agent.PostAndAsyncReply(fun reply -> GetStream(identifier, reply))

                member _.AllStreamsOf streamType =
                    agent.PostAndAsyncReply(fun reply -> Get(streamType, reply))

                member _.Append events =
                    agent.PostAndAsyncReply(fun reply -> Append(events, reply)) 
            
                member _.All () =
                    agent.PostAndAsyncReply(fun reply -> GetAll(reply)) } 

type EventStore
    private
    (
        storage: Storage.EventStorage,
        // TODO: use an agent for subscriptions?!
        subscriptions: ConcurrentDictionary<System.Type, SubscriptionWrapper list>
    ) =

    let subscriptionsOf key =
        let (success, items) = subscriptions.TryGetValue key
        if success then items else []

    let asTyped items : EventEnvelope<'E> list = items |> List.map EventEnvelope.unbox

    let asUntyped items = items |> List.map EventEnvelope.box

    let notifySubscriptions (newItems: EventEnvelope<'E> list) =
        subscriptionsOf typedefof<'E>
        |> List.map
            (fun subscription ->
                let upcastSubscription events = events |> asUntyped |> subscription

                upcastSubscription newItems)
        |> Async.Sequential
        |> Async.Ignore

    let appendAndNotify (newItems: EventEnvelope<'E> list) =
        async {
            do! storage.Append(newItems |> asUntyped)
            do! notifySubscriptions newItems
        }

    let subscribe (subscription: Subscription<'E>) =
        let key = typedefof<'E>

        let upcastSubscription events = events |> asTyped |> subscription

        subscriptions.AddOrUpdate(
            key,
            (fun _ -> [ upcastSubscription ]),
            (fun _ subscriptions -> subscriptions @ [ upcastSubscription ])
        )
        |> ignore

    let allStreams () : Async<EventEnvelope<'E> list> =
        async {
            match! StreamKind.Of<'E>() |> storage.AllStreamsOf with
            | Ok allStreams -> return allStreams |> asTyped
            | Error e ->
                failwithf "Could not get all streams: %s" e
                return List.empty
        }

    let stream name : Async<EventEnvelope<'E> list> =
        async {
            match! storage.Stream(name, StreamKind.Of<'E>()) with
            | Ok events -> return events |> asTyped
            | Error e ->
                failwithf "Could not get stream %s" e
                return List.empty
        }
        
    let all toAllStream : Async<EventEnvelope<'E> list> =
        async {
            match! storage.All() with
            | Ok allStreams -> return allStreams |> toAllStream
            | Error e ->
                failwithf "Could not get all streams: %s" e
                return List.empty
        }

    static member Empty =
        EventStore(Storage.InMemoryStorage.initialize List.empty, ConcurrentDictionary())

    static member With(history: EventEnvelope list) =
        EventStore(Storage.InMemoryStorage.initialize history, ConcurrentDictionary())

    member _.Stream name = stream name
    member _.Append items = appendAndNotify items
    member _.AllStreams() = allStreams ()
    member _.Subscribe(subscription: Subscription<'E>) = subscribe subscription
    member _.All toAllStream = all toAllStream

module Projections =
    type Projection<'State, 'Event> =
        { Init: 'State
          Update: 'State -> 'Event -> 'State }

    let projectIntoMap selectId projection =
        fun state (eventEnvelope: EventEnvelope<_>) ->
            let selectedId = selectId eventEnvelope

            state
            |> Map.tryFind selectedId
            |> Option.defaultValue projection.Init
            |> fun projectionState ->
                eventEnvelope.Event
                |> projection.Update projectionState
            |> fun newState -> state |> Map.add selectedId newState

    let projectIntoMapBySourceId projection =
        projectIntoMap (fun eventEnvelope -> eventEnvelope.Metadata.Source) projection

    let project projection (events: EventEnvelope<_> list) =
        events
        |> List.map (fun e -> e.Event)
        |> List.fold projection.Update projection.Init


module ReadModels =
    type EventHandler<'Event> = EventEnvelope<'Event> list -> Async<unit>

    type ReadModelInitialization =
        abstract member ReplayAndConnect : unit -> Async<unit>

    module ReadModelInitialization =
        type private RMI<'Event>(eventStore: EventStore, handler: EventHandler<'Event>) =
            interface ReadModelInitialization with
                member __.ReplayAndConnect() =
                    async {
                        let! allStreams = eventStore.AllStreams<'Event>()
                        do! handler allStreams
                        eventStore.Subscribe handler
                    }

        let initializeWith (eventStore: EventStore) (handler: EventHandler<'Event>) : ReadModelInitialization =
            RMI(eventStore, handler) :> ReadModelInitialization

    type ReadModel<'Event, 'State> =
        abstract member EventHandler : EventEnvelope<'Event> list -> Async<unit>
        abstract member State : unit -> Task<'State>

    type Msg<'Event, 'Result> =
        | Notify of EventEnvelope<'Event> list * AsyncReplyChannel<unit>
        | State of AsyncReplyChannel<'Result>

    let readModel
        (updateState: 'State -> EventEnvelope<'Event> list -> 'State)
        (initState: 'State)
        : ReadModel<'Event, 'State> =
        let agent =
            let eventSubscriber (inbox: Agent<Msg<_, _>>) =
                let rec loop state =
                    async {
                        let! msg = inbox.Receive()

                        match msg with
                        | Notify (eventEnvelopes, reply) ->
                            reply.Reply()
                            return! loop (eventEnvelopes |> updateState state)

                        | State reply ->
                            reply.Reply state
                            return! loop state
                    }

                loop initState

            Agent<Msg<_, _>>.Start (eventSubscriber)

        { new ReadModel<'Event, 'State> with
            member _.EventHandler eventEnvelopes =
                agent.PostAndAsyncReply(fun reply -> Notify(eventEnvelopes, reply))

            member _.State() =
                agent.PostAndAsyncReply State |> Async.StartAsTask }
