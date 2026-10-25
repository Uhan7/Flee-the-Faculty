Edit speaker dialogue in Assets/Resources/Dialogue Speaker Templates.

Open Character, Teacher, Student, or AraBOT as a prefab. Select its speaker
name or body text and edit the TextMeshPro font, font size, alignment, margins,
or character/word/line spacing. Move or resize the body text rectangle to change
the space available for dialogue. The box, name label, and arrow can also be
edited in the prefab.

The body previews “Lorem ipsum dolor sit amet, lorem ipsum dolor sit amet.”
This is preview text only. Conversations still supply their real dialogue and
speaker names. Back/X behaviour and scrolling are shared across all speakers.

The initial templates preserve the scene's existing presentation. Font/spacing
changes belong here; the legacy font fields on SceneDialogueView are fallbacks
when a template is missing. Student speakers share the Student prefab.

If a template is deleted, use Flee the Faculty > Dialogue > Create Missing
Speaker Templates to recreate it from the classroom scene. Existing templates
are kept intact.
