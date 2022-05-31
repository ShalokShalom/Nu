﻿// Nu Game Engine.
// Copyright (C) Bryan Edds, 2013-2020.

namespace Nu
open System

/// IO artifacts passively produced and consumed by Nu.
type [<NoEquality; NoComparison>] View =
    | Render2d of single * single * obj AssetTag * RenderDescriptor
    | PlaySound of single * Sound AssetTag
    | PlaySong of int * int * single * double * Song AssetTag
    | FadeOutSong of int
    | StopSong
    | SpawnEmitter of string * Particles.BasicEmitterDescriptor
    | Tag of string * obj
    //| Schedule of (World -> World)
    | Views of View array
    | Views2 of View * View
    | SegmentedViews of View SegmentedArray

[<RequireQualifiedAccess>]
module View =

    /// Convert a view to an seq of zero or more views.
    let rec toSeq view =
        seq {
            match view with
            | Views views -> for view in views do yield! toSeq view
            | _ -> yield view }

    /// The empty view.
    let empty = Views [||]
