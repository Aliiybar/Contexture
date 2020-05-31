module Bcc.Edit exposing (Msg, Model, update, view, init)

import Browser.Navigation as Nav

import Html exposing (Html, button, div, text)
import Html.Attributes exposing (..)
import Html.Events exposing (onClick)

import Bootstrap.Grid as Grid
import Bootstrap.Grid.Row as Row
import Bootstrap.Grid.Col as Col
import Bootstrap.Form as Form
import Bootstrap.Form.Input as Input
import Bootstrap.Form.Select as Select
import Bootstrap.Form.Textarea as Textarea
import Bootstrap.Form.Radio as Radio
import Bootstrap.Button as Button

import Url
import Http

import Route
import Bcc
import Bcc.Edit.Dependencies as Dependencies
import Bcc.Edit.Messages as Messages

-- MODEL

type alias EditingCanvas = 
  { canvas : Bcc.BoundedContextCanvas
  , addingMessage : Messages.AddingMessage
  , addingDependencies: Dependencies.DependenciesEdit
  }

type alias Model = 
  { key: Nav.Key
  , self: Url.Url
  -- TODO: discuss we want this in edit or BCC - it's not persisted after all!
  , edit: EditingCanvas
  }

init : Nav.Key -> Url.Url -> (Model, Cmd Msg)
init key url =
  let
    canvas = Bcc.init ()
    model =
      { key = key
      , self = url
      , edit = 
        { addingMessage = Messages.initAddingMessage
        , addingDependencies = Dependencies.initDependencies
        , canvas = canvas
        }
      }
  in
    (
      model
    , loadBCC model
    )

-- UPDATE

type EditingMsg
  = Field Bcc.Msg
  | MessageField Messages.Msg
  | DependencyField Dependencies.Msg

type Msg
  = Loaded (Result Http.Error Bcc.BoundedContextCanvas)
  | Editing EditingMsg
  | Save
  | Saved (Result Http.Error ())
  | Delete
  | Deleted (Result Http.Error ())
  | Back

updateEdit : EditingMsg -> EditingCanvas -> EditingCanvas
updateEdit msg model =
  case msg of
    MessageField messageMsg ->
      let
        (addingMessage, messages) = Messages.update messageMsg (model.addingMessage, model.canvas.messages)
        canvas = model.canvas
        c = { canvas | messages = messages}
      in
        { model | addingMessage = addingMessage, canvas = c }
    Field fieldMsg ->
      { model | canvas = Bcc.update fieldMsg model.canvas }
    DependencyField dependency ->
      let 
        (addingDependencies, dependencies) = Dependencies.update dependency (model.addingDependencies, model.canvas.dependencies)
        canvas = model.canvas
        c = { canvas | dependencies = dependencies}
      in
        { model | canvas = c, addingDependencies = addingDependencies }
    

update : Msg -> Model -> (Model, Cmd Msg)
update msg model =
  case msg of
    Editing editing ->
      ({ model | edit = updateEdit editing model.edit}, Cmd.none)
    Save -> 
      (model, saveBCC model)
    Saved (Ok _) -> 
      (model, Cmd.none)
    Delete ->
      (model, deleteBCC model)
    Deleted (Ok _) ->
      (model, Route.pushUrl Route.Overview model.key)
    Loaded (Ok m) ->
      let
        editing = 
          { canvas = m
          , addingMessage = Messages.initAddingMessage
          , addingDependencies = Dependencies.initDependencies
          }
      in
        ({ model | edit = editing } , Cmd.none)    
    Back -> 
      (model, Route.goBack model.key)
    _ ->
      Debug.log ("BCC: " ++ Debug.toString msg ++ " " ++ Debug.toString model)
      (model, Cmd.none)

-- VIEW

view : Model -> Html Msg
view model =
  div []
      [ viewCanvas model.edit |> Html.map Editing
      , Grid.row []
        [ Grid.col [] 
          [ Button.button [Button.secondary, Button.onClick Back] [text "Back"]
          , Button.submitButton [ Button.primary, Button.onClick Save ] [ text "Save"]
          , Button.button 
            [ Button.danger
            , Button.onClick Delete
            , Button.attrs [ title ("Delete " ++ model.edit.canvas.name) ] 
            ]
            [ text "X" ]
          ]
        ]
      ]

viewRadioButton : String -> String -> Bool -> Bcc.Msg -> Radio.Radio Bcc.Msg
viewRadioButton id title checked msg =
  Radio.create [Radio.id id, Radio.onClick msg, Radio.checked checked] title

viewLeftside : Bcc.BoundedContextCanvas -> List (Html EditingMsg)
viewLeftside model =
  [ Form.group []
    [ Form.label [for "name"] [ text "Name"]
    , Input.text [ Input.id "name", Input.value model.name, Input.onInput Bcc.SetName ] ]
  , Form.group []
    [ Form.label [for "description"] [ text "Description"]
    , Input.text [ Input.id "description", Input.value model.description, Input.onInput Bcc.SetDescription ]
    , Form.help [] [ text "Summary of purpose and responsibilities"] ]
  , Grid.row []
    [ Grid.col [] 
      [ Form.label [for "classification"] [ text "Bounded Context classification"]
      , div [] 
          (Radio.radioList "classification" 
          [ viewRadioButton "core" "Core" (model.classification == Just Bcc.Core) (Bcc.SetClassification Bcc.Core) 
          , viewRadioButton "supporting" "Supporting" (model.classification == Just Bcc.Supporting) (Bcc.SetClassification Bcc.Supporting) 
          , viewRadioButton "generic" "Generic" (model.classification == Just Bcc.Generic) (Bcc.SetClassification Bcc.Generic) 
          -- TODO: Other
          ]
          )
      , Form.help [] [ text "How can the Bounded Context be classified?"] ]
      , Grid.col []
        [ Form.label [for "businessModel"] [ text "Business Model"]
        , div [] 
            (Radio.radioList "businessModel" 
            [ viewRadioButton "revenue" "Revenue" (model.businessModel == Just Bcc.Revenue) (Bcc.SetBusinessModel Bcc.Revenue) 
            , viewRadioButton "engagement" "Engagement" (model.businessModel == Just Bcc.Engagement) (Bcc.SetBusinessModel Bcc.Engagement) 
            , viewRadioButton "Compliance" "Compliance" (model.businessModel == Just Bcc.Compliance) (Bcc.SetBusinessModel Bcc.Compliance) 
            , viewRadioButton "costReduction" "Cost reduction" (model.businessModel == Just Bcc.CostReduction) (Bcc.SetBusinessModel Bcc.CostReduction) 
            -- TODO: Other
            ]
            )
        , Form.help [] [ text "What's the underlying business model of the Bounded Context?"] ]
      , Grid.col []
        [ Form.label [for "evolution"] [ text "Evolution"]
        , div [] 
            (Radio.radioList "evolution" 
            [ viewRadioButton "genesis" "Genesis" (model.evolution == Just Bcc.Genesis) (Bcc.SetEvolution Bcc.Genesis) 
            , viewRadioButton "customBuilt" "Custom built" (model.evolution == Just Bcc.CustomBuilt) (Bcc.SetEvolution Bcc.CustomBuilt) 
            , viewRadioButton "product" "Product" (model.evolution == Just Bcc.Product) (Bcc.SetEvolution Bcc.Product) 
            , viewRadioButton "commodity" "Commodity" (model.evolution == Just Bcc.Commodity) (Bcc.SetEvolution Bcc.Commodity) 
            -- TODO: Other
            ]
            )
        , Form.help [] [ text "How does the context evolve? How novel is it?"] ]
    ]
  , Form.group []
    [ Form.label [for "businessDecisions"] [ text "Business Decisions"]
      , Textarea.textarea [ Textarea.id "businessDecisions", Textarea.rows 4, Textarea.value model.businessDecisions, Textarea.onInput Bcc.SetBusinessDecisions ]
      , Form.help [] [ text "Key business rules, policies and decisions"] ]
  , Form.group []
    [ Form.label [for "ubiquitousLanguage"] [ text "Ubiquitous Language"]
      , Textarea.textarea [ Textarea.id "ubiquitousLanguage", Textarea.rows 4, Textarea.value model.ubiquitousLanguage, Textarea.onInput Bcc.SetUbiquitousLanguage ]
      , Form.help [] [ text "Key domain terminology"] ]
  ]
  |> List.map (Html.map Field)

viewRightside : EditingCanvas -> List (Html EditingMsg)
viewRightside model =
  [ Form.group []
    [ Form.label [for "modelTraits"] [ text "Model traits"]
    , Input.text [ Input.id "modelTraits", Input.value model.canvas.modelTraits, Input.onInput Bcc.SetModelTraits ] |> Html.map Field
    , Form.help [] [ text "draft, execute, audit, enforcer, interchange, gateway, etc."] ]
    , (model.addingMessage, model.canvas.messages) |> Messages.view |> Html.map MessageField
    , (model.addingDependencies, model.canvas.dependencies) |> Dependencies.view |> Html.map DependencyField
  ]

viewCanvas : EditingCanvas -> Html EditingMsg
viewCanvas model =
  Grid.row []
    [ Grid.col [] (viewLeftside model.canvas)
    , Grid.col [] (viewRightside model)
    ]

-- HTTP

loadBCC: Model -> Cmd Msg
loadBCC model =
  Http.get
    { url = Url.toString model.self
    , expect = Http.expectJson Loaded Bcc.modelDecoder
    }

saveBCC: Model -> Cmd Msg
saveBCC model =
    Http.request
      { method = "PUT"
      , headers = []
      , url = Url.toString model.self
      , body = Http.jsonBody <| Bcc.modelEncoder model.edit.canvas
      , expect = Http.expectWhatever Saved
      , timeout = Nothing
      , tracker = Nothing
      }

deleteBCC: Model -> Cmd Msg
deleteBCC model =
    Http.request
      { method = "DELETE"
      , headers = []
      , url = Url.toString model.self
      , body = Http.emptyBody
      , expect = Http.expectWhatever Deleted
      , timeout = Nothing
      , tracker = Nothing
      }