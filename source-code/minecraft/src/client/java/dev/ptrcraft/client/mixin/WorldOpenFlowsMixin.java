package dev.ptrcraft.client.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import dev.ptrcraft.SkyCraft;
import net.minecraft.client.gui.screens.worldselection.WorldOpenFlows;
import net.minecraft.world.level.storage.LevelStorageSource;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;

/**
 * The mirror world's custom dimension type marks its worldgen as "experimental", which makes
 * Minecraft ask for a backup every time it opens. Skip that prompt for the PTRCraft worlds only.
 */
@Mixin(WorldOpenFlows.class)
public abstract class WorldOpenFlowsMixin {
	@WrapOperation(
		method = "openWorldCheckWorldStemCompatibility",
		at = @At(
			value = "INVOKE",
			target = "Lnet/minecraft/client/gui/screens/worldselection/WorldOpenFlows;askForBackup(Lnet/minecraft/world/level/storage/LevelStorageSource$LevelStorageAccess;ZLjava/lang/Runnable;Ljava/lang/Runnable;)V"
		)
	)
	private void skycraft$skipBackupPrompt(
		WorldOpenFlows self, LevelStorageSource.LevelStorageAccess access, boolean oldCustomized, Runnable proceed, Runnable cancel, Operation<Void> original
	) {
		if (access.getLevelId().startsWith(SkyCraft.WORLD_NAME)) { // PTRCraft and PTRCraft-<level>
			proceed.run();
		} else {
			original.call(self, access, oldCustomized, proceed, cancel);
		}
	}
}
