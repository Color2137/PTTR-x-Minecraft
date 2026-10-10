package dev.ptrcraft.mixin;

import dev.ptrcraft.SkyCraft;
import dev.ptrcraft.combat.SkyCombat;
import dev.ptrcraft.combat.SkyrimActorEntity;
import dev.ptrcraft.link.Proto;
import dev.ptrcraft.link.SkyLink;
import net.minecraft.world.damagesource.DamageSource;
import net.minecraft.world.entity.Entity;
import net.minecraft.server.level.ServerPlayer;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(ServerPlayer.class)
public abstract class ServerPlayerMixin {
	/** Critical hits on a Skyrim actor are flagged so Skyrim can play them up. */
	@Inject(method = "crit", at = @At("HEAD"))
	private void skycraft$critSkyrim(Entity entity, CallbackInfo ci) {
		if (entity instanceof SkyrimActorEntity proxy) {
			proxy.markCritical();
		}
	}

	/** Dying in Minecraft is dying in Skyrim: the host's through the link, a guest's through theirs. */
	@Inject(method = "die", at = @At("HEAD"))
	private void skycraft$diesInSkyrim(DamageSource source, CallbackInfo ci) {
		ServerPlayer self = (ServerPlayer) (Object) this;
		int attacker = SkyCombat.attackerFormId(source);
		if (!dev.ptrcraft.net.SkyNet.isHost(self)) {
			if (net.fabricmc.fabric.api.networking.v1.ServerPlayNetworking.canSend(self, dev.ptrcraft.net.SkyNet.Died.TYPE)) {
				net.fabricmc.fabric.api.networking.v1.ServerPlayNetworking.send(self, new dev.ptrcraft.net.SkyNet.Died(attacker));
			}
			SkyCraft.LOG.info("SkyCraft: guest {} died ({}); telling their Skyrim", self.getPlainTextName(), source.getMsgId());
			return;
		}
		if (SkyLink.active()) {
			SkyLink.pushEvent(Proto.EV_PLAYER_DIED, attacker, 0, 0, 0, 0, 0);
			SkyCraft.LOG.info("SkyCraft: player died ({}); telling Skyrim", source.getMsgId());
		}
	}
}
