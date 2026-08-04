@tool
extends Node

## Installed under MainServer by the World Chunk Streamer plugin (owner left null).
## Forwards editor save notifications so the plugin can block baking streamed objects.

var plugin: EditorPlugin = null


func _notification(what: int) -> void:
	if plugin == null:
		return
	if what == NOTIFICATION_EDITOR_PRE_SAVE:
		if plugin.has_method("handle_editor_pre_save"):
			plugin.handle_editor_pre_save()
	elif what == NOTIFICATION_EDITOR_POST_SAVE:
		if plugin.has_method("handle_editor_post_save"):
			plugin.handle_editor_post_save()
