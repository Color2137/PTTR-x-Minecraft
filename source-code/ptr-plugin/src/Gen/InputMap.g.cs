// <generated> by tools/sheets.mjs from sheets/input_map.json. Edit the sheet, not this file.
namespace PTRCraft
{
	internal static class InputMap
	{
		public static readonly (string id, string input, string when, string goesTo)[] All =
		{
			("keys", "every key except Esc, O and G", "in a level, PTR focused, no PTR menu", "minecraft"),
			("mouse_buttons", "left / right / middle / side buttons", "same", "minecraft"),
			("wheel", "mouse wheel", "same", "minecraft"),
			("look", "mouse movement", "no Minecraft screen open", "ptr"),
			("cursor", "mouse position", "a Minecraft screen is open (inventory, chat, menus)", "minecraft"),
			("text", "typed characters", "a Minecraft screen is open", "minecraft"),
			("esc", "Esc", "no Minecraft screen open", "ptr"),
			("esc_close", "Esc", "a Minecraft screen is open", "minecraft"),
			("o_menu", "O", "no Minecraft screen open", "minecraft"),
			("g_use", "G", "no Minecraft screen open", "ptr"),
			("f5", "F5", "always", "minecraft"),
			("focus_lost", "Alt-Tab / PTR loses focus", "always", "minecraft"),
		};
	}
}
// </generated>
