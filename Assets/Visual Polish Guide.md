# Visual Polish Rig

The shared defaults live in `Assets/Resources/Visual Polish Rig.prefab`. Each main scene now contains an instance of that prefab, including real editable light and particle children that are visible before Play Mode.

## Change every main scene

1. In Unity, use **Tools > Flee the Faculty > Visual Polish > Select Shared Defaults**.
2. Change any setting on the `Scene Visual Polish` component in the Inspector.
3. The room lights and dust update in the editor. Enter Play Mode to preview camera post-processing.

## Change only one scene

1. Open that scene.
2. Use **Tools > Flee the Faculty > Visual Polish > Add Scene Override**.
3. Change the new `Visual Polish Rig - Scene Override` object in the Inspector.
4. Save the scene and enter Play Mode.

The automatic fallback rig stays out of the way whenever a scene already contains its own visible rig.

## Edit lights directly in a scene

1. Expand **Visual Polish Rig > Room Accent Lights (Editable)** in the Hierarchy.
2. Select any **Room Light** child and use Unity's normal Transform and Light 2D controls. These manual edits are preserved by default when entering Play Mode.
3. **Auto Layout Room Lights** is disabled by default. Enable it only when you deliberately want the root's Count, Center, and Spacing settings to reposition the children.
4. **Sync Room Light Properties** is disabled by default. Enable it only when you deliberately want the root to overwrite every child's color, intensity, radius, and falloff.

**Global Light Enabled** on the rig is also disabled by default. This means edits made directly to the scene's Global Light are preserved. Enable the rig option only when you intentionally want its Global Light Color and Intensity values applied during Play Mode.

Eight light children are available. The current Count decides how many are active, so the setup can scale without generating new objects during Play Mode.

## Edit dust directly in a scene

Expand **Visual Polish Rig** and select **Ambient Dust (Editable)**. It is a normal Particle System, so Unity's particle preview and module controls are available before Play Mode. Manual particle modules and positioning are preserved by default.

Enable **Sync Dust Particle Properties** only if you want the root rig to overwrite the particle modules from its safe settings. Enable **Dust Follows Camera** only if you want runtime camera tracking instead of the transform position you set manually.

If the prefab children ever go missing, use **Tools > Flee the Faculty > Visual Polish > Build Editable Rig In Main Scenes** to rebuild and reinstall them.

## Safe tuning order

1. **Global Light Color** and **Intensity** set the overall room mood.
2. **Room Accent Lights** controls the number, center, spacing, color, intensity, and radius of the visible light pools.
3. **Contrast**, **Saturation**, **Temperature**, and **Color Filter** make the art feel cohesive.
4. Keep **Bloom Intensity** below about `0.35` unless you deliberately want a dreamy look.
5. Keep **Vignette Intensity** below about `0.15` so the corners do not look dirty.
6. Raise or lower **Dust Emission Rate** before changing particle count.
7. Adjust character shadow width, height, and opacity to match future character sizes. Newly spawned students are detected automatically.

Student and AraBOT shadows are real children of their prefabs, so you can open either prefab and see the result immediately. Select the root's **Soft Contact Shadow** component to adjust its color, width, height, and vertical offset. Use **Tools > Flee the Faculty > Visual Polish > Rebuild Visible Prefab Shadows** only if the shared shadow texture or prefab children need to be restored.

Each section has its own checkbox. Disabling a section removes its runtime cost and makes A/B comparisons easy.
