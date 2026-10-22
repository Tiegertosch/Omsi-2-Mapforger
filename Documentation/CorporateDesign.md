# OMSI MapForge -- Corporate Design & Brand Identity

**Version:** 1.0\
**Status:** Initial design guideline\
**Purpose:** Visual and verbal foundation for the OMSI MapForge project,
editor UI, documentation, website, and community communication.

------------------------------------------------------------------------

## 1. Brand foundation

### Project name

**OMSI MapForge**

A modern, standalone map editor concept for OMSI 2, focused on intuitive
map building, visual editing, and a more efficient workflow.

### Brand idea

**Build worlds. Shape roads. Drive your vision.**

OMSI MapForge combines the precision of a technical editor with the
creative freedom of world building. The brand should feel like a
professional desktop tool for experienced map creators while remaining
approachable to new users.

### Brand promise

Make map building clearer, more visual, and more manageable---without
losing sight of the technical requirements of OMSI 2 compatibility.

This is a project vision, not a claim that all planned features are
already implemented.

### Brand values

-   **Precise:** Clear alignment, predictable tools, and measurable
    parameters.
-   **Modern:** A contemporary interface that avoids the visual language
    of legacy editors.
-   **Practical:** Important actions are easy to find and quick to use.
-   **Modular:** Tools can grow independently without overloading the
    UI.
-   **Reliable:** Editing, saving, and data handling should be
    transparent and safe.
-   **Community-driven:** Real map-building workflows and feedback guide
    priorities.

### Brand personality

Technical, confident, restrained, and creative. It should not look like
a toy, a generic gaming launcher, or an overly futuristic sci-fi
interface.

**Tone:** professional, direct, constructive, and community-friendly.

------------------------------------------------------------------------

## 2. Visual direction

The existing concepts suggest a **dark, compact desktop interface** with
a muted blue-charcoal foundation, fine borders, readable typography, and
warm orange highlights for active tools. Panels float over the map
viewport with subtle depth rather than heavy decoration.

The interface should prioritize the map. UI chrome must support the
work, not compete with it.

### Core principles

1.  **Viewport first:** Keep as much space as possible for the map.
2.  **Clear hierarchy:** Separate global navigation, tool selection,
    contextual settings, and object properties.
3.  **Orange means active:** Use the accent color primarily for selected
    tools, active states, primary actions, and important feedback.
4.  **Consistent icons:** Use one coherent line-icon family and avoid
    mixing unrelated icon styles.
5.  **Labels where needed:** Essential tools should have text labels or
    discoverable tooltips.
6.  **Progressive disclosure:** Show frequently used options first;
    place advanced controls in expandable sections.
7.  **Low visual noise:** Avoid excessive gradients, bright outlines,
    unnecessary animations, and decorative headings.
8.  **Accessible contrast:** Text and controls must remain legible
    against dark panels and the map.

------------------------------------------------------------------------

## 3. Color system

These are initial design tokens inferred from the visual concepts.
Validate them in the actual application and adjust for contrast and
usability.

  ------------------------------------------------------------------------
  Token                    Hex                     Intended use
  ------------------------ ----------------------- -----------------------
  `color-bg`               `#171C23`               Main application
                                                   background

  `color-panel`            `#202630`               Primary panels and
                                                   toolbars

  `color-panel-raised`     `#29313D`               Cards, nested panels,
                                                   secondary controls

  `color-panel-hover`      `#35475A`               Hover and focused
                                                   surfaces

  `color-border`           `#414B59`               Dividers and subtle
                                                   panel borders

  `color-text-primary`     `#F2F4F7`               Main text and important
                                                   labels

  `color-text-secondary`   `#AEB8C5`               Secondary labels,
                                                   hints, metadata

  `color-accent`           `#F08A32`               Active tools, primary
                                                   actions, key highlights

  `color-accent-hover`     `#FF9B45`               Hover state for accent
                                                   controls

  `color-success`          `#55B887`               Successful validation
                                                   and completed
                                                   operations

  `color-warning`          `#E6B450`               Warnings requiring
                                                   attention

  `color-error`            `#E06464`               Errors, destructive
                                                   actions, failed
                                                   validation

  `color-info`             `#65A9D9`               Informational hints and
                                                   neutral guidance
  ------------------------------------------------------------------------

### Color rules

-   Orange is the main brand accent, not a background color for large
    areas.
-   Use neutral dark surfaces for most UI elements.
-   Red is reserved for destructive actions and errors; do not use it as
    a second brand accent.
-   Green, yellow, and blue should communicate status only.
-   Avoid relying on color alone to convey state. Add icons, labels, or
    shape changes.
-   Verify text contrast and focus visibility before release.

------------------------------------------------------------------------

## 4. Typography

Use a modern, neutral sans-serif typeface.

-   **Primary UI font:** Inter, if bundled/licensed appropriately;
    otherwise use a supported system sans-serif.
-   **Fallback:** Segoe UI on Windows.
-   **Technical values:** Use tabular numerals where possible for
    coordinates, dimensions, and measurements.
-   **Code and IDs:** Use a monospace font for file paths, resource IDs,
    and diagnostics.

  Level                     Suggested size Use
  ----------------------- ---------------- ----------------------------------------
  Page or project title          20--24 px Current map or workspace name
  Panel heading                  14--16 px Inspector, project browser, tool panel
  Standard UI text               13--14 px Controls, labels, menus
  Secondary text                 11--12 px Hints, metadata, keyboard shortcuts
  Dense technical data           11--13 px Coordinates, resource paths, IDs

These are starting points for a desktop UI at normal scaling. Support
Windows display scaling and user-adjustable UI scale where feasible.

### Typography rules

-   Use sentence case rather than all caps for most visible labels.
-   Use medium or semibold weights for panel headings and active tool
    names.
-   Avoid bold for every label.
-   Do not use decorative display fonts in the editor.
-   Keep line lengths short in dense inspector panels.

------------------------------------------------------------------------

## 5. Iconography

Use one consistent line-icon system throughout the application.
Lucide-style outline icons are a suitable starting point, provided their
license and integration method are verified.

-   Prefer simple, recognizable metaphors.
-   Use consistent stroke width and optical size.
-   Keep icons visually centered in buttons.
-   Pair unfamiliar or important icons with text labels.
-   Show keyboard shortcuts in compact keycaps where useful.
-   Use an accent-backed treatment for the active tool.
-   Do not use emoji as production UI icons.

### Core icon groups

-   Selection and transform: select, move, rotate, scale, delete.
-   World editing: terrain, roads, splines, objects, vegetation,
    markings.
-   Navigation: camera, frame selection, fit map, top-down view.
-   Precision: grid, snapping, height, measurement.
-   Project: save, undo, redo, validate, export, settings.

------------------------------------------------------------------------

## 6. Layout and component system

### Overall application structure

1.  **Top menu bar:** File, Edit, View, Tools, Map, Window, Help.
2.  **Primary viewport:** The main 2D or 3D map editing area.
3.  **Central bottom toolbar:** Frequently used tools available without
    moving to a side panel.
4.  **Contextual tool panel:** Settings specific to the selected tool,
    such as road mode, spline resource, snapping, or terrain brush.
5.  **Project browser:** Tiles, map elements, resources, and project
    structure.
6.  **Inspector:** Properties of the current selection.
7.  **Status bar:** Coordinates, active tile, selection count,
    validation status, and save state.

Panels should be resizable and collapsible. Remember panel visibility
and sizing in workspace preferences if practical.

### Central bottom toolbar

The bottom toolbar sits inside the viewport, above its lower edge. It
should float visually without obscuring critical map content.

Recommended groups: - **Selection:** Select, move, rotate, scale,
delete. - **World:** Terrain, roads, splines, objects, vegetation,
markings. - **Utilities:** Measure, camera, settings. - **Precision:**
Grid, snapping, height. - **Overflow:** Less frequently used tools.

Use thin vertical separators between groups. Keep button sizes
consistent. On narrow windows, collapse labels before removing essential
tools; expose overflow for less frequently used commands.

### Contextual tool panels

Selecting a tool opens or updates its corresponding contextual panel.

-   **Roads:** build type, draw mode, spline resource, snapping, height
    handling.
-   **Terrain:** brush mode, radius, strength, falloff.
-   **Objects:** asset library, search, categories, placement options.
-   **Splines:** spline resource, control points, connection options.
-   **Measure:** distance, area, height, angle.

Do not display every possible setting simultaneously. Put advanced
settings in expandable sections.

### Selection and feedback

-   Selected items must be visibly highlighted in both 2D and 3D.
-   Hover and selected states must be distinct.
-   Multi-selection should be clearly indicated.
-   Destructive actions should have clear labels and confirmation where
    data loss is possible.
-   Show contextual tooltips with purpose and keyboard shortcut.
-   Use status messages for operations that may take time or fail.

------------------------------------------------------------------------

## 7. Interaction design

### Input principles

-   Prefer predictable, reversible operations.
-   Avoid assigning the same shortcut to conflicting actions.
-   Make active tools obvious.
-   Keep common actions one click away.
-   Support keyboard shortcuts without making them the only access
    method.
-   Keep mouse interactions consistent across 2D and 3D views where
    possible.

### Provisional keyboard shortcuts

These are starting suggestions, not final assignments. Check for
conflicts with Windows, Unity, and existing project bindings.

  Action            Proposed shortcut
  ----------------- ----------------------------
  Select            `Q`
  Move              `W`
  Rotate            `E`
  Scale             `R`
  Delete            `Delete`
  Terrain           `T`
  Roads             `1`
  Splines           `2`
  Objects           `3`
  Vegetation        `4`
  Markings          `5`
  Measure           `M`
  Camera tool       `C`
  Grid toggle       `Z`
  Snapping toggle   `X`
  Height controls   `Y`
  Undo              `Ctrl+Z`
  Redo              `Ctrl+Y` or `Ctrl+Shift+Z`

The application should eventually provide a configurable shortcut map
and a visible shortcut reference.

### Editing safety

-   Implement undo/redo for meaningful edits.
-   Keep temporary previews separate from committed map data.
-   Provide clear feedback when an operation is rejected.
-   Avoid silently changing unsupported or unknown OMSI data.
-   Make save state visible.
-   Keep backups before risky conversion or export operations.

------------------------------------------------------------------------

## 8. Brand voice and messaging

### Short description

**OMSI MapForge is a modern map editor concept for OMSI 2, designed to
make map building more visual, intuitive, and efficient.**

### Tagline

**Build worlds. Shape roads. Drive your vision.**

### German tagline alternative

**Welten bauen. Straßen gestalten. Deine Vision fahren.**

### Tone of voice

-   Clear rather than exaggerated.
-   Technical but understandable.
-   Ambitious but honest about development status.
-   Community-focused without promising unimplemented features.

Preferred wording: - "Planned feature" - "In development" - "The goal is
to support..." - "This prototype demonstrates the intended workflow."

Avoid claiming that unimplemented features already work, promising full
compatibility before testing, or calling the editor a complete
replacement before it is ready.

------------------------------------------------------------------------

## 9. Logo and brand assets

The current concepts establish a visual direction but do not yet define
a final logo. Treat the name and wordmark as provisional until a
dedicated logo is designed.

### Suggested direction

-   A clean geometric wordmark: **OMSI MapForge**.
-   Use a restrained orange accent, preferably on "Forge" or a small
    symbol.
-   Optional symbol: an abstract road curve intersecting a node, forming
    a subtle "M" or connected path.
-   Avoid copying OMSI or Cities: Skylines branding, logos, or
    distinctive UI assets.
-   Ensure the logo works in monochrome, at small sizes, and on dark or
    light backgrounds.

### Asset variants to prepare later

-   Full horizontal logo.
-   Compact icon.
-   Monochrome version.
-   Light-background and dark-background versions.
-   App icon and favicon.
-   Social/community avatar.
-   GitHub repository banner.

Review the project name and relevant trademark considerations before
finalizing a logo.

------------------------------------------------------------------------

## 10. Accessibility and usability

-   Maintain adequate contrast between text, icons, and surfaces.
-   Never rely on orange versus gray alone to communicate state.
-   Include tooltips for unfamiliar tools.
-   Keep keyboard navigation and focus indicators visible.
-   Support Windows display scaling.
-   Avoid tiny hit targets for frequently used tools.
-   Use consistent terminology across tooltips, documentation, and the
    interface.
-   Test the UI with long resource names and localized labels.
-   Provide clear error messages rather than only changing an icon or
    color.

------------------------------------------------------------------------

## 11. Implementation guidance for Unity UI Toolkit

Represent colors, spacing, typography, radii, and component sizes as
shared design tokens rather than repeating raw values throughout
stylesheets.

Suggested token names:

``` text
--mf-color-bg
--mf-color-panel
--mf-color-panel-raised
--mf-color-border
--mf-color-text-primary
--mf-color-text-secondary
--mf-color-accent
--mf-color-success
--mf-color-warning
--mf-color-error

--mf-space-1
--mf-space-2
--mf-space-3
--mf-space-4

--mf-radius-sm
--mf-radius-md
--mf-radius-lg
```

Use reusable UI components for tool buttons, tool groups and separators,
toggles, numeric fields, sliders, dropdowns, collapsible settings,
tooltips, keyboard keycaps, and status messages.

Separate styling from tool logic. The UI should issue commands or update
settings models; it should not directly manipulate road graph data or
generate geometry.

Do not create a large component framework prematurely. Implement only
components needed by the current feature, then extract reusable patterns
when they appear more than once.

------------------------------------------------------------------------

## 12. Non-goals

This document is a design guideline, not a claim that the interface or
features already exist.

-   Do not recreate Cities: Skylines II's exact UI.
-   Do not copy third-party icons, textures, or branding.
-   Do not implement all tool categories at once.
-   Do not force every tool into the bottom toolbar.
-   Do not use orange for every button or panel.
-   Do not build a fully animated interface before core editing
    workflows work.

------------------------------------------------------------------------

## 13. Acceptance checklist

-   [ ] Dark blue-charcoal base with restrained orange active states.
-   [ ] Readable typography and consistent iconography.
-   [ ] Viewport remains the visual priority.
-   [ ] Bottom toolbar is grouped and context-aware.
-   [ ] Tool panels reveal relevant settings without overwhelming users.
-   [ ] Project browser and inspector remain visually distinct.
-   [ ] Selected, hovered, disabled, warning, and error states are
    distinguishable.
-   [ ] Keyboard shortcuts are visible where useful and can eventually
    be customized.
-   [ ] Design tokens are shared across UI components.
-   [ ] No visual or textual implication that unimplemented features are
    complete.
-   [ ] Layout remains usable at different desktop sizes and scaling
    settings.

------------------------------------------------------------------------

## 14. Summary

OMSI MapForge should look like a professional, modern technical editor:
dark and restrained, with a clear information hierarchy and warm orange
accents that guide attention to the active tool. The map stays central,
the bottom toolbar provides quick access to common operations, and
contextual panels expose only the settings relevant to the current task.

**Brand principle:** The interface should make complex map-building
operations feel clear, controlled, and approachable.
