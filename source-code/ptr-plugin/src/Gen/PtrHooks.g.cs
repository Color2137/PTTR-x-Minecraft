// <generated> by tools/sheets.mjs from sheets/ptr_hooks.json. Edit the sheet, not this file.
namespace PTRCraft
{
	internal static class PtrHooks
	{
		/// <summary>(id, class, member, kind): everything the plugin touches in PTR.</summary>
		public static readonly (string id, string cls, string member, string kind)[] All =
		{
			("player_find", "PTTRPlayer", "IsLocalPlayer", "call"),
			("player_feet", "PTTRPlayer", "GetFeetPosition", "call"),
			("fps_controller", "PTTRFirstPersonController", "Update", "disable"),
			("mouse_rotators", "SimpleMouseRotator", "LateUpdate", "disable"),
			("weapon_attack", "WeaponAttack", "Update", "disable"),
			("ptr_use", "WeaponAttack", "InteractWithWeaponOrButton", "call"),
			("drop_weapon", "WeaponAttack", "DropWeapon", "call"),
			("beneath_item", "BeneathItemPickup", "Awake", "read"),
			("hands_hidden", "PTTRPlayer", "bodyRenderers", "hide"),
			("camera_fov", "CameraFOV", "OnEnable", "disable"),
			("char_controller", "CharacterController", "enabled", "disable"),
			("set_health", "PTTRPlayer", "SetHealth", "prefix"),
			("get_punched", "PTTRPlayer", "GetPunched", "prefix"),
			("fall_damage", "PTTRPlayer", "FallDamage", "prefix"),
			("enemy_damage", "Enemy", "Damage", "call"),
			("enemy_start_combat", "Enemy", "StartCombat", "call"),
			("enemy_impact", "Enemy", "AnimateImpact", "call"),
			("enemy_force", "Enemy", "ApplyImpactForce", "call"),
			("enemy_state", "Enemy", "health", "read"),
			("game_paused", "GameManager", "Paused", "read"),
			("game_in_menu", "GameManager", "InMenu", "read"),
			("cursor", "GameManager", "SetCursorAppropriate", "prefix"),
			("player_dead", "PTTRPlayer", "IsDead", "read"),
			("load_level", "GameManager", "LoadLevel", "call"),
			("hud_panels", "GameHUD", "LateUpdate", "hide"),
			("controls_help", "ControlsHelp", "canvasGroup", "hide"),
			("colliders", "MeshCollider", "sharedMesh", "read"),
			("pirate_cove_night", "PaintTheTownRed", "PirateCoveNight", "read"),
			("custom_level_file", "LevelEditor", "LevelFileToPlay", "read"),
			("arena_challenge", "Arena", "CurrentChallengeIndex", "read"),
			("endless_arena", "GameManager", "IsEndlessArena", "read"),
			("beneath_area", "Beneath", "CurrentArea", "read"),
			("beneath_floor", "Beneath", "CurrentLevel", "read"),
			("beneath_trickster_room", "Beneath", "CurrentTricksterRoom", "read"),
			("beneath_area_names", "Beneath", "AreaNames", "read"),
			("collider_raycast", "Collider", "Raycast", "call"),
			("character_controller", "CharacterController", "isGrounded", "read"),
			("trigger_overlap", "Physics", "OverlapCapsuleNonAlloc", "call"),
			("trigger_messages", "GameObject", "SendMessage", "call"),
			("beneath_exit", "BeneathLevelExit", "OnTriggerEnter", "read"),
			("trigger_people", "Component", "GetComponent", "call"),
			("beneath_transition", "Beneath", "InTransition", "read"),
			("enemy_set_on_fire", "Enemy", "SetOnFire", "call"),
			("enemy_on_fire", "Enemy", "onFire", "read"),
			("is_beneath", "GameManager", "IsBeneath", "read"),
			("editor_creating", "LevelEditorPlay", "CreatingObjects", "read"),
			("editor_started", "LevelEditorPlay", "StartedPlaying", "read"),
			("editor_testing", "LevelEditor", "TestingLevel", "read"),
			("editor_level_name", "LevelEditor", "CurrentLevelSimpleName", "read"),
		};
	}
}
// </generated>
