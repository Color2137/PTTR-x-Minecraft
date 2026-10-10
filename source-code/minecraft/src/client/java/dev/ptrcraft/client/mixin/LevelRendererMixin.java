package dev.ptrcraft.client.mixin;

import com.mojang.blaze3d.framegraph.FrameGraphBuilder;
import com.mojang.renderpearl.api.buffers.GpuBufferSlice;
import dev.ptrcraft.client.SkyClient;
import dev.ptrcraft.client.render.PtrOccluders;
import net.minecraft.client.renderer.LevelRenderer;
import net.minecraft.client.renderer.LevelTargetBundle;
import net.minecraft.client.renderer.chunk.ChunkSectionsToRender;
import net.minecraft.client.renderer.feature.FeatureRenderDispatcher;
import net.minecraft.client.renderer.state.level.LevelRenderState;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.ModifyVariable;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/**
 * PTR draws the world around you; Minecraft draws only its own things on top of it.
 *
 * PTRCraft: Minecraft renders its level (placed blocks, mobs, other players, arrows, dropped
 * items, particles, your F5 body, block outlines and cracks) into the overlay, on the transparent
 * background its clear pass leaves, with no sky or clouds. PTR's geometry and people go into the
 * depth buffer first (PtrOccluders) so whatever is behind them stays hidden. When PTR asks to draw
 * Minecraft's blocks itself (setting MinecraftDrawsWorld off), Minecraft renders no level at all,
 * as in SkyCraft: the overlay is just hand + HUD.
 */
@Mixin(LevelRenderer.class)
public abstract class LevelRendererMixin {
	private static final String RENDER =
		"render(Lcom/mojang/blaze3d/resource/GraphicsResourceAllocator;ZLnet/minecraft/client/renderer/state/level/CameraRenderState;Lcom/mojang/renderpearl/api/buffers/GpuBufferSlice;Lorg/joml/Vector4f;ZZ)V";

	@Shadow
	@Final
	private LevelTargetBundle targets;

	@Shadow
	@Final
	private LevelRenderState levelRenderState;

	@Inject(method = RENDER, at = @At("HEAD"), cancellable = true)
	private void ptrcraft$skipLevel(CallbackInfo ci) {
		if (SkyClient.linked() && !SkyClient.drawsWorld()) {
			ci.cancel();
		}
	}

	/** PTR's sky stays visible: Minecraft's sky pass is never added while linked. */
	@ModifyVariable(method = RENDER, at = @At("HEAD"), argsOnly = true, ordinal = 1)
	private boolean ptrcraft$noSky(boolean shouldRenderSky) {
		return shouldRenderSky && !SkyClient.linked();
	}

	@Inject(method = "addMainPass", at = @At("HEAD"))
	private void ptrcraft$occluders(
		FrameGraphBuilder frame,
		FeatureRenderDispatcher.PreparedFrame featureFrame,
		GpuBufferSlice terrainFog,
		ChunkSectionsToRender chunkSectionsToRender,
		boolean consistentDepthRequired,
		CallbackInfo ci
	) {
		if (SkyClient.drawsWorld()) {
			PtrOccluders.addPass(frame, this.targets, this.levelRenderState.cameraRenderState);
		}
	}
}
