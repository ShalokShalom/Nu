﻿namespace Tactics
open System
open System.Collections.Generic
open System.Numerics
open Prime
open TiledSharp
open Nu

type [<StructuralEquality; NoComparison>] Occupant =
    | Character of Character
    | Chest of unit

type [<StructuralEquality; StructuralComparison>] OccupantIndex =
    | AllyIndex of int
    | EnemyIndex of int
    | ChestIndex of int

type NarrativeState =
    | NarrativeResult of bool

type BattleState =
    | BattleReady of int64
    | BattleCharacterReady of int64 // camera moving to character
    | BattleCharacterMenu of CharacterIndex // using ring menu or AI
    | BattleCharacterMoving of CharacterIndex // character moving to destination
    | BattleCharacterAttacking of CharacterIndex * CharacterIndex
    | BattleCharacterTeching
    | BattleCharacterConsuming
    | BattleResult of int64 * bool

type FieldState =
    | FieldReady of int64 // field fades in
    | NarrativeState of NarrativeState
    | BattleState of BattleState
    | FieldQuitting of int64 * bool // field fades out

type FieldScript =
    | FieldToBattle
    | FieldToNarrative
    | FieldCondition of FieldScript * FieldScript
    | FieldScripts of FieldScript list
    static member empty = FieldScripts []

type [<NoEquality; NoComparison>] FieldMetadata =
    { FieldVertexMap : Map<Vector2i, Vector3 array>
      FieldUntraversableSurfaceDescriptor : StaticModelSurfaceDescriptor
      FieldTraversableSurfaceDescriptor : StaticModelSurfaceDescriptor
      FieldBounds : Box3 }

[<RequireQualifiedAccess>]
module Field =

    type [<ReferenceEquality; NoComparison>] Field =
        private
            { FieldState_ : FieldState
              FieldScript : FieldScript
              FieldTileMap : TileMap AssetTag
              OccupantIndices : Map<Vector2i, OccupantIndex list>
              OccupantPositions : Map<OccupantIndex, Vector2i>
              Occupants : Map<OccupantIndex, Occupant>
              SelectedTile : Vector2i }

        member this.FieldState = this.FieldState_

    let private CachedFieldMetadata = dictPlus<TileMap AssetTag, FieldMetadata> HashIdentity.Structural []

    let private createFieldSurfaceDescriptorAndVertexMap tileMapWidth tileMapHeight (tileSets : TmxTileset array) (tileLayer : TmxLayer) (heightLayer : TmxLayer) =

        // compute bounds
        let heightScalar = 0.5f
        let heightMax = 16.0f * heightScalar
        let position = v3Zero
        let bounds = box3 position (v3 (single tileMapWidth) (heightMax * 2.0f) (single tileMapHeight))

        // make positions array
        let positions = Array.zeroCreate<Vector3> (tileMapWidth * tileMapHeight * 6)

        // initialize positions flat, centered
        let offset = v3 (single tileMapWidth * -0.5f) 0.0f (single tileMapHeight * -0.5f)
        for i in 0 .. dec tileMapWidth do
            for j in 0 .. dec tileMapHeight do
                let t = j * tileMapWidth + i
                let tile = heightLayer.Tiles.[t]
                let mutable tileSetOpt = None
                for tileSet in tileSets do
                    let tileZero = tileSet.FirstGid
                    let tileCount = let opt = tileSet.TileCount in opt.GetValueOrDefault 0
                    match tileSetOpt with
                    | None ->
                        if  tile.Gid >= tileZero &&
                            tile.Gid < tileZero + tileCount then
                            tileSetOpt <- Some tileSet
                    | Some _ -> ()
                let height =
                    match tileSetOpt with
                    | None -> 0.0f
                    | Some tileSet -> single (tile.Gid - tileSet.FirstGid) * heightScalar
                let u = t * 6
                let position = v3 (single i) height (single j) + offset
                positions.[u] <- position
                positions.[u+1] <- position + v3Right
                positions.[u+2] <- position + v3Right + v3Forward
                positions.[u+3] <- position
                positions.[u+4] <- position + v3Right + v3Forward
                positions.[u+5] <- position + v3Forward

        // slope positions horizontal
        for i in 0 .. dec tileMapWidth do
            for j in 0 .. dec tileMapHeight do
                if j % 2 = 0 then
                    let t = j * tileMapWidth + i
                    let tNorth = t - tileMapWidth
                    if tNorth >= 0 then
                        let u = t * 6
                        let uNorth = tNorth * 6
                        positions.[u+5].Y <- positions.[uNorth].Y
                        positions.[u+2].Y <- positions.[uNorth+1].Y
                        positions.[u+4].Y <- positions.[uNorth+1].Y
                    let tSouth = t + tileMapWidth
                    if tSouth < tileMapWidth * tileMapHeight then
                        let u = t * 6
                        let uSouth = tSouth * 6
                        positions.[u].Y <- positions.[uSouth+5].Y
                        positions.[u+3].Y <- positions.[uSouth+5].Y
                        positions.[u+1].Y <- positions.[uSouth+2].Y

        // slope positions vertical
        for i in 0 .. dec tileMapWidth do
            if i % 2 = 0 then
                for j in 0 .. dec tileMapHeight do
                    let t = j * tileMapWidth + i
                    let tWest = t - 1
                    if tWest >= 0 then
                        let u = t * 6
                        let uWest = tWest * 6
                        positions.[u].Y <- positions.[uWest+1].Y
                        positions.[u+3].Y <- positions.[uWest+1].Y
                        positions.[u+5].Y <- positions.[uWest+2].Y
                    let tEast = t + 1
                    if tEast < tileMapWidth * tileMapHeight then
                        let u = t * 6
                        let uEast = tEast * 6
                        positions.[u+1].Y <- positions.[uEast].Y
                        positions.[u+2].Y <- positions.[uEast+5].Y
                        positions.[u+4].Y <- positions.[uEast+5].Y

        // make vertex map in-place
        let mutable vertexMap = Map.empty

        // populate vertex map
        for i in 0 .. dec tileMapWidth do
            for j in 0 .. dec tileMapHeight do
                let t = j * tileMapWidth + i
                let u = t * 6
                let vertices =
                    [|positions.[u]
                      positions.[u+1]
                      positions.[u+2]
                      positions.[u+5]|]
                vertexMap <- Map.add (v2i i j) vertices vertexMap

        // make tex coordses array
        let texCoordses = Array.zeroCreate<Vector2> (tileMapWidth * tileMapHeight * 6)

        // populate tex coordses
        let mutable albedoTileSetOpt = None
        for i in 0 .. dec tileMapWidth do
            for j in 0 .. dec tileMapHeight do
                let t = j * tileMapWidth + i
                let tile = tileLayer.Tiles.[t]
                let mutable tileSetOpt = None
                for tileSet in tileSets do
                    let tileZero = tileSet.FirstGid
                    let tileCount = let opt = tileSet.TileCount in opt.GetValueOrDefault 0
                    match albedoTileSetOpt with
                    | None ->
                        if tile.Gid = 0 then
                            tileSetOpt <- Some tileSet // just use the first tile set for the empty tile
                        elif tile.Gid >= tileZero && tile.Gid < tileZero + tileCount then
                            tileSetOpt <- Some tileSet
                            albedoTileSetOpt <- tileSetOpt // use tile set that is first to be non-zero
                    | Some _ -> tileSetOpt <- albedoTileSetOpt
                match tileSetOpt with
                | Some tileSet ->
                    let tileId = tile.Gid - tileSet.FirstGid
                    let tileImageWidth = let opt = tileSet.Image.Width in opt.Value
                    let tileImageHeight = let opt = tileSet.Image.Height in opt.Value
                    let tileWidthNormalized = single tileSet.TileWidth / single tileImageWidth
                    let tileHeightNormalized = single tileSet.TileHeight / single tileImageHeight
                    let tileXCount = let opt = tileSet.Columns in opt.Value
                    let tileX = tileId % tileXCount
                    let tileY = tileId / tileXCount + 1
                    let texCoordX = single tileX * tileWidthNormalized
                    let texCoordY = single tileY * tileHeightNormalized
                    let texCoordX2 = texCoordX + tileWidthNormalized
                    let texCoordY2 = texCoordY - tileHeightNormalized
                    let u = t * 6
                    texCoordses.[u] <- v2 texCoordX texCoordY
                    texCoordses.[u+1] <- v2 texCoordX2 texCoordY
                    texCoordses.[u+2] <- v2 texCoordX2 texCoordY2
                    texCoordses.[u+3] <- v2 texCoordX texCoordY
                    texCoordses.[u+4] <- v2 texCoordX2 texCoordY2
                    texCoordses.[u+5] <- v2 texCoordX texCoordY2
                | None -> ()

        // make normals array
        let normals = Array.zeroCreate<Vector3> (tileMapWidth * tileMapHeight * 6)

        // populate normals
        for i in 0 .. dec tileMapWidth do
            for j in 0 .. dec tileMapHeight do
                let t = j * tileMapWidth + i
                let u = t * 6
                let a = positions.[u]
                let b = positions.[u+1]
                let c = positions.[u+5]
                let normal = Vector3.Normalize (Vector3.Cross (b - a, c - a))
                normals.[u] <- normal
                normals.[u+1] <- normal
                normals.[u+2] <- normal
                normals.[u+3] <- normal
                normals.[u+4] <- normal
                normals.[u+5] <- normal

        // create indices
        let indices = Array.init (tileMapWidth * tileMapHeight * 6) id;

        // ensure we've found an albedo tile set
        match albedoTileSetOpt with
        | Some albedoTileSet ->

            // create static model surface descriptor
            let descriptor =
                { Positions = positions
                  TexCoordses = texCoordses
                  Normals = normals
                  Indices = indices
                  AffineMatrix = m4Identity
                  Bounds = bounds
                  Albedo = Color.White
                  AlbedoImage = albedoTileSet.ImageAsset
                  Metalness = 0.0f
                  MetalnessImage = Assets.Default.MaterialMetalness
                  Roughness = 1.2f
                  RoughnessImage = Assets.Default.MaterialRoughness
                  AmbientOcclusion = 1.0f
                  AmbientOcclusionImage = albedoTileSet.ImageAsset
                  NormalImage = Assets.Default.MaterialNormal
                  TextureMinFilterOpt = ValueSome OpenGL.TextureMinFilter.NearestMipmapNearest
                  TextureMagFilterOpt = ValueSome OpenGL.TextureMagFilter.Nearest
                  TwoSided = false }

            // fin
            (descriptor, vertexMap)

        // did not find albedo tile set
        | None -> failwith "Unable to find custom TmxLayer Image property; cannot create tactical map."

    let getFieldMetadata (field : Field) world =
        match CachedFieldMetadata.TryGetValue field.FieldTileMap with
        | (false, _) ->
            let (_, tileSetsAndImages, tileMap) = World.getTileMapMetadata field.FieldTileMap world
            let tileSets = Array.map fst tileSetsAndImages
            let untraversableLayer = tileMap.Layers.["Untraversable"] :?> TmxLayer
            let untraversableHeightLayer = tileMap.Layers.["UntraversableHeight"] :?> TmxLayer
            let traversableLayer = tileMap.Layers.["Traversable"] :?> TmxLayer
            let traversableHeightLayer = tileMap.Layers.["TraversableHeight"] :?> TmxLayer
            let (untraversableSurfaceDescriptor, _) = createFieldSurfaceDescriptorAndVertexMap tileMap.Width tileMap.Height tileSets untraversableLayer untraversableHeightLayer
            let untraversableSurfaceDescriptor = { untraversableSurfaceDescriptor with Roughness = 0.1f }
            let (traversableSurfaceDescriptor, traversableVertexMap) = createFieldSurfaceDescriptorAndVertexMap tileMap.Width tileMap.Height tileSets traversableLayer traversableHeightLayer
            let bounds = let bounds = untraversableSurfaceDescriptor.Bounds in bounds.Combine traversableSurfaceDescriptor.Bounds
            let fieldMetadata =
                { FieldVertexMap = traversableVertexMap
                  FieldUntraversableSurfaceDescriptor = untraversableSurfaceDescriptor
                  FieldTraversableSurfaceDescriptor = traversableSurfaceDescriptor
                  FieldBounds = bounds }
            CachedFieldMetadata.Add (field.FieldTileMap, fieldMetadata)
            fieldMetadata
        | (true, fieldMetadata) -> fieldMetadata

    let tryGetVertices index field world =
        let fieldMetadata = getFieldMetadata field world
        match Map.tryFind index fieldMetadata.FieldVertexMap with
        | Some index -> Some index
        | None -> None

    let getVertices index field world =
        match tryGetVertices index field world with
        | Some vertices -> vertices
        | None -> failwith ("Field vertex index '" + scstring index + "' out of range.")

    let tryGetFieldTileDataAtMouse field world =
        let mouseRay = World.getMouseRay3dWorld false world
        let fieldMetadata = getFieldMetadata field world
        let indices = [|0; 1; 2; 0; 2; 3|]
        let intersectionMap =
            Map.map (fun _ vertices ->
                let intersections = mouseRay.Intersects (indices, vertices)
                match Seq.tryHead intersections with
                | Some struct (_, intersection) -> Some (intersection, vertices)
                | None -> None)
                fieldMetadata.FieldVertexMap
        let intersections =
            intersectionMap |>
            Seq.map (fun (kvp : KeyValuePair<_, _>) -> (kvp.Key, kvp.Value)) |>
            Seq.filter (fun (_, opt) -> opt.IsSome) |>
            Seq.map (fun (key, Some (a, b)) -> (key, a, b)) |>
            Seq.toArray |>
            Array.sortBy Triple.snd |>
            Array.tryHead
        intersections

    let rec advanceFieldScript field (world : World) =
        match field.FieldState_ with
        | BattleState (BattleResult (_, result))
        | NarrativeState (NarrativeResult result) ->
            match field.FieldScript with
            | FieldToBattle -> { field with FieldState_ = BattleState (BattleReady (world.UpdateTime + 60L)) }
            | FieldToNarrative -> { field with FieldState_ = NarrativeState (NarrativeResult false) }
            | FieldCondition (consequent, alternative) -> advanceFieldScript { field with FieldScript = if result then consequent else alternative } world
            | FieldScripts scripts ->
                match scripts with
                | _ :: scripts -> advanceFieldScript { field with FieldScript = FieldScripts scripts } world
                | _ -> { field with FieldState_ = FieldQuitting (world.UpdateTime + 60L, result) }
        | _ -> field

    let advance field world =
        let field = advanceFieldScript field world
        field

    let make updateTime fieldScript tileMap =
        { FieldState_ = FieldReady updateTime
          FieldScript = fieldScript
          FieldTileMap = tileMap
          OccupantIndices = Map.empty
          OccupantPositions = Map.empty
          Occupants = Map.empty
          SelectedTile = v2iZero }

type Field = Field.Field