# Image Gallery

The Gallery is a unified view of every image in your project. Reference photos, character portraits, location concept art, mood boards — all in one place, regardless of which entity (if any) they're attached to.

## Opening the Gallery

In the activity bar, click **Gallery** in the **World** group.

## What it shows

The Gallery lists every image in the project's image folder. Each entry shows a thumbnail and the image's name. A "*n* of *m*" count at the top right reports how many images are shown out of the total.

Thumbnails are loaded lazily — large galleries fill in as you scroll.

## Grid and list views

A toggle in the toolbar switches between two layouts:

- **Grid** (default) — a wall of thumbnail cards captioned with each filename.
- **List** — one row per image with a small thumbnail, the image's name, and its full path.

## Filtering

The **search box** at the top filters images by filename or path (substring match). Useful when you remember "the dragon image" but not which entity you attached it to.

Filenames alone are a poor index — a folder of four hundred references is navigable only by whatever your browser happened to call each file when you saved it. Two more filters sit beside the search box once there is something to filter by:

- **Collection** — the one collection a picture belongs to, or **Every collection**.
- **Tag** — anything else you have said about it, or **Every tag**.

Both pickers list only what is actually in use, and they appear only once something has been filed. A picker whose sole entry reads "everything" is a control that cannot do anything.

## Collections and tags

Right-click a picture and choose **File into a collection...** or **Tags...**.

A picture belongs to **one collection** and any number of **tags**. Nothing moves on disk: a picture is already pointed at by scenes, entries, banners, and map layers by its path, and filing it into a real folder would move it and break every one of them. The filing lives in a sidecar file in the project, so it travels with the project and survives it being zipped or moved.

Two collections differing only in capitals are one collection, and the same tag typed twice is one tag — a picker offering both spellings guarantees you file into both. Clearing a picture's collection and tags removes its row entirely rather than leaving an empty one behind.

## Previewing an image

Click any thumbnail to open it in a full-screen **lightbox** with its filename. Click anywhere to close.

## Image actions

Right-click a thumbnail (in either view) for a context menu:

- **Copy Path** — copies the image's project-relative path to the clipboard.
- **Copy as Markdown** — copies a Markdown image reference (`![name](path)`).
- **Open Externally** — opens the image in your default image application.
- **Reveal** — shows the image in your system file manager.

## Adding images

**Import pictures** in the toolbar copies files in from disk. As everywhere else in Novalist, they are copied rather than pointed at: a path into your Downloads folder is a file that will be gone by the time anyone follows it, and a project has to survive being zipped or moved to another machine.

You can also add images from the **Codex**: every entity's detail pane has an image strip with buttons to add **From gallery** (pick an image already in the project), **Import image** (copy a file in from disk), **Paste image** (from the clipboard), and **From URL** (download an image by address). See [Codex](06-codex.md).

Images dropped into the project's image folder with a file manager also appear in the Gallery.

## Images used in the prose

Pictures inserted into a scene with [Insert image](05-editor.md#images-in-the-prose) live in the same `Images` folder and appear in this gallery alongside the ones attached to Codex entries.

## Where to go next

- [Codex](06-codex.md) — entity image strips, where images are attached.
- [Research](15-research.md) — reference notes to pair with your images.
