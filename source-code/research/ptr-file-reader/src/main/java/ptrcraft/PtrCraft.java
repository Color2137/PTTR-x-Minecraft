package ptrcraft;

import net.fabricmc.api.ModInitializer;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

public final class PtrCraft implements ModInitializer {
	public static final String MOD_ID = "ptrcraft";
	public static final Logger LOG = LoggerFactory.getLogger("PTRCraft");

	@Override
	public void onInitialize() {
		LOG.info("PTRCraft loading");
	}
}
