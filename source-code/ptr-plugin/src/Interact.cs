using System;

namespace PTRCraft
{
	/// <summary>
	/// G = PTR's "use": press buttons, take Beneath items and upgrades, buy in shops, through PTR's own
	/// WeaponAttack.InteractWithWeaponOrButton (its attack script is off while Minecraft plays, but the
	/// method still works on what the camera looks at). Ordinary PTR weapons are dropped again at
	/// once: Minecraft does the fighting.
	/// </summary>
	internal static class Interact
	{
		public static int Uses;
		public static string Last = "";

		public static void Use()
		{
			var attack = Puppet.Attack;
			if (attack == null)
			{
				Last = "no PTR player";
				return;
			}
			Uses++;
			try
			{
				var before = attack.currentWeapon;
				attack.InteractWithWeaponOrButton(true, true, true);
				var now = attack.currentWeapon;
				if (now != null && (before == null || now.Pointer != before.Pointer))
				{
					bool item = now.TryCast<BeneathItemPickup>() != null;
					if (!item) attack.DropWeapon(true);
					Last = item ? $"took {now.name}" : $"PTR weapon {now.name} dropped (Minecraft fights)";
				}
				else Last = "used what the crosshair is on";
			}
			catch (Exception e)
			{
				Last = "failed: " + e.Message;
				Plugin.L.LogWarning("PTRCraft: PTR use failed: " + e);
			}
		}
	}
}
