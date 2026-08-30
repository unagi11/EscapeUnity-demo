local sprite = app.sprite

if not sprite then
  app.alert("Open a sprite before exporting.")
  return
end

if sprite.filename == "" then
  app.alert("Save the sprite before exporting.")
  return
end

local sourceDir = app.fs.filePath(sprite.filename)
local assetsDir = app.fs.filePath(sourceDir)
local outputDir = app.fs.joinPath(assetsDir, "Resources", "Sheets")

if not app.fs.isDirectory(outputDir) then
  app.alert("Expected a Unity Assets/Resources/Sheets folder next to the Artworks folder.")
  return
end

local projectName = app.fs.fileTitle(sprite.filename)
local outputName = projectName .. "-sheet"
local textureFilename = app.fs.joinPath(outputDir, outputName .. ".png")
local dataFilename = app.fs.joinPath(outputDir, outputName .. ".json")

local visibilityStates = {}

local function setAllLayersVisible(layers)
  for _, layer in ipairs(layers) do
    table.insert(visibilityStates, { layer=layer, visible=layer.isVisible })
    layer.isVisible = true
    if layer.isGroup then
      setAllLayersVisible(layer.layers)
    end
  end
end

local function restoreLayerVisibility()
  for i=#visibilityStates, 1, -1 do
    local state = visibilityStates[i]
    state.layer.isVisible = state.visible
  end
end

local function showResult(title, message)
  if not app.isUIAvailable then
    print(title .. ": " .. message)
    print("PNG: " .. textureFilename)
    print("JSON: " .. dataFilename)
    return
  end

  Dialog(title)
    :label{ text=message }
    :label{ label="PNG:", text=textureFilename }
    :label{ label="JSON:", text=dataFilename }
    :button{ text="OK", focus=true }
    :show()
end

local exported, exportError = pcall(function()
  setAllLayersVisible(sprite.layers)
  app.command.ExportSpriteSheet {
    ui=false,
    recent=false,
    askOverwrite=false,
    type=SpriteSheetType.PACKED,
    textureFilename=textureFilename,
    dataFilename=dataFilename,
    dataFormat=SpriteSheetDataFormat.JSON_HASH,
    filenameFormat="{layer}__frame_{frame}",
    borderPadding=0,
    shapePadding=0,
    innerPadding=0,
    trimSprite=true,
    trim=true,
    trimByGrid=true,
    extrude=true,
    ignoreEmpty=false,
    mergeDuplicates=true,
    openGenerated=false,
    splitLayers=true,
    splitTags=false,
    splitGrid=false,
    listLayers=true,
    listTags=true,
    listSlices=true,
    fromTilesets=false,
  }
end)
restoreLayerVisibility()

if not exported then
  showResult("Unity Room Sheet Export Failed", tostring(exportError))
  return
end

if not app.fs.isFile(textureFilename) or not app.fs.isFile(dataFilename) then
  showResult("Unity Room Sheet Export Failed", "The PNG or JSON file was not created.")
  return
end

showResult("Unity Room Sheet Export Complete", "Export completed.")
