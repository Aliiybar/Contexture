namespace Contexture.Api.ReadModels.Find

open System
open Contexture.Api.Infrastructure
open Contexture.Api.ReadModels
open Contexture.Api.ReadModels.Find

module SearchFor =
    module NamespaceId =
        open Contexture.Api.Aggregates.Namespace
        open ValueObjects

        [<CLIMutable>]
        type NamespaceQuery =
            { Template: NamespaceTemplateId []
              Name: string [] }
            member this.IsActive =
                this.Name.Length > 0 || this.Template.Length > 0

        let findRelevantNamespaces (availableNamespaces: Namespaces.NamespaceFinder) (item: NamespaceQuery) =
                let namespacesByName =
                    item.Name
                    |> SearchArgument.fromInputs
                    |> SearchArgument.executeSearch (Find.Namespaces.byName availableNamespaces)

                let namespacesByTemplate =
                    item.Template
                    |> SearchArgument.fromValues
                    |> SearchArgument.executeSearch (Find.Namespaces.byTemplate availableNamespaces)

                SearchResult.combineResults
                    [ namespacesByName
                      namespacesByTemplate ]

        let find (state:  Namespaces.NamespaceFinder) (byNamespace: NamespaceQuery option) =
            byNamespace
            |> Option.map (findRelevantNamespaces state)
            |> Option.map(SearchResult.map (fun n -> n.NamespaceId))
            |> SearchResult.fromOption

    module Labels =

        [<CLIMutable>]
        type LabelQuery =
            { Name: string []
              Value: string [] }
            member this.IsActive =
                not (Seq.isEmpty (Seq.append this.Name this.Value))

        let findRelevantLabels (namespacesByLabel: Labels.NamespacesByLabel) (item: LabelQuery) =
            let byNameResults =
                item.Name
                |> SearchArgument.fromInputs
                |> SearchArgument.executeSearch (Find.Labels.byLabelName namespacesByLabel)


            let byLabelResults =
                item.Value
                |> SearchArgument.fromInputs
                |> SearchArgument.executeSearch (Find.Labels.byLabelValue namespacesByLabel)

            SearchResult.combineResults [ byNameResults
                                          byLabelResults ]

        let find (state: Labels.NamespacesByLabel) (byLabel: LabelQuery option) =
            byLabel
            |> Option.map (findRelevantLabels state)
            |> SearchResult.fromOption

    module DomainId =
        [<CLIMutable>]
        type DomainQuery =
            { Name: string []
              Key: string [] }
            member this.IsActive =
                not (Seq.isEmpty (Seq.append this.Name this.Key))

        let findRelevantDomains (findDomains: Domains.DomainByKeyAndNameModel) (query: DomainQuery) =
            let foundByName =
                query.Name
                |> SearchArgument.fromInputs
                |> SearchArgument.executeSearch (Find.Domains.byName findDomains)

            let foundByKey =
                query.Key
                |> SearchArgument.fromInputs
                |> SearchArgument.executeSearch (Find.Domains.byKey findDomains)

            SearchResult.combineResults [ foundByName
                                          foundByKey ]


        let find (state: Domains.DomainByKeyAndNameModel) (query: DomainQuery option) =
            query
            |> Option.map (findRelevantDomains state)
            |> SearchResult.fromOption

    module BoundedContextId =
        [<CLIMutable>]
        type BoundedContextQuery =
            { Name: string []
              Key: string [] }
            member this.IsActive =
                not (Seq.isEmpty (Seq.append this.Name this.Key))

        let findRelevantBoundedContexts
            (findBoundedContext: Find.BoundedContexts.BoundedContextByKeyAndNameModel)
            (query: BoundedContextQuery)
            =
            let foundByName =
                query.Name
                |> SearchArgument.fromInputs
                |> SearchArgument.executeSearch (Find.BoundedContexts.byName findBoundedContext)

            let foundByKey =
                query.Key
                |> SearchArgument.fromInputs
                |> SearchArgument.executeSearch (Find.BoundedContexts.byKey findBoundedContext)

            SearchResult.combineResults [ foundByName
                                          foundByKey ]

        let find (state: BoundedContexts.BoundedContextByKeyAndNameModel) (query: BoundedContextQuery option) =
            query
            |> Option.map (findRelevantBoundedContexts state)
            |> SearchResult.fromOption
