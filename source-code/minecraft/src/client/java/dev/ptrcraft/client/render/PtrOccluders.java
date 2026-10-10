package dev.ptrcraft.client.render;

import com.mojang.blaze3d.framegraph.FrameGraphBuilder;
import com.mojang.blaze3d.framegraph.FramePass;
import com.mojang.blaze3d.pipeline.RenderTarget;
import com.mojang.blaze3d.resource.ResourceHandle;
import com.mojang.blaze3d.systems.RenderSystem;
import com.mojang.renderpearl.api.GpuFormat;
import com.mojang.renderpearl.api.buffers.GpuBuffer;
import com.mojang.renderpearl.api.buffers.GpuBufferSlice;
import com.mojang.renderpearl.api.commands.RenderPass;
import com.mojang.renderpearl.api.pipeline.BlendFunction;
import com.mojang.renderpearl.api.pipeline.ColorTargetState;
import com.mojang.renderpearl.api.pipeline.CompareOp;
import com.mojang.renderpearl.api.pipeline.DepthStencilState;
import com.mojang.renderpearl.api.pipeline.PrimitiveTopology;
import com.mojang.renderpearl.api.pipeline.RenderPipeline;
import dev.ptrcraft.SkyCraft;
import dev.ptrcraft.link.SkyLink;
import dev.ptrcraft.world.SkyCollision;
import dev.ptrcraft.world.SkyTri;
import it.unimi.dsi.fastutil.floats.FloatArrayList;
import it.unimi.dsi.fastutil.longs.LongOpenHashSet;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.ArrayList;
import java.util.List;
import java.util.Optional;
import java.util.OptionalDouble;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import net.minecraft.client.renderer.LevelTargetBundle;
import net.minecraft.client.renderer.RenderPipelines;
import net.minecraft.client.renderer.state.level.CameraRenderState;
import net.minecraft.resources.Identifier;
import net.minecraft.world.phys.Vec3;
import org.joml.Matrix4f;

/**
 * PTR's level as an invisible layer in Minecraft's depth buffer. Minecraft draws its own blocks,
 * mobs, arrows, items and the F5 body into the overlay; PTR's walls, floors, furniture and people
 * must still hide what is behind them. So right before Minecraft's main pass this draws, depth
 * only, the exact triangles PTR sent as collision (SkyCollision) and a box for each standing PTR
 * person (the actor table). Render thread, except the level mesh, which is built on a worker.
 */
public final class PtrOccluders {
	private static final RenderPipeline PIPELINE = RenderPipeline.builder(RenderPipelines.WATER_MASK_SNIPPET)
		.withLocation(Identifier.fromNamespaceAndPath("ptrcraft", "pipeline/ptr_occluders"))
		.withColorTargetState(new ColorTargetState(Optional.of(BlendFunction.TRANSLUCENT), GpuFormat.RGBA8_UNORM, 0))
		.withPrimitiveTopology(PrimitiveTopology.TRIANGLES)
		.withCull(false)
		// A little further from the camera than the surface: Minecraft things resting on PTR's
		// floors and tables (items, mobs' feet, placed blocks) don't flicker into them.
		.withDepthStencilState(new DepthStencilState(CompareOp.GREATER_THAN_OR_EQUAL, true, -1.0F, -8.0F))
		.build();

	private static final int VERTEX_BYTES = 12; // DefaultVertexFormat.POSITION: 3 floats
	private static final int MAX_PEOPLE = 96;
	private static final double PEOPLE_RANGE = 64.0;
	private static final float BODY_WIDTH = 0.8F; // of the stand-in's box: closer to the body's outline
	private static final long REBUILD_NANOS = 500_000_000L; // while PTR streams collision in

	private static final ExecutorService WORKER = Executors.newSingleThreadExecutor(r -> {
		Thread t = new Thread(r, "PTRCraft occluders");
		t.setDaemon(true);
		return t;
	});

	private record Built(int version, ByteBuffer data, int vertices, int triangles, double ox, double oy, double oz) {
	}

	// PTR's level: static triangles relative to an origin near where they were built.
	private static GpuBuffer level;
	private static int levelVertices;
	private static double ox, oy, oz;
	private static int builtVersion = Integer.MIN_VALUE;
	private static CompletableFuture<Built> building;
	private static long lastBuild;
	private static int buildErrors;

	// PTR's people: rebuilt every frame, relative to the camera.
	private static GpuBuffer people;
	private static final ByteBuffer PEOPLE = ByteBuffer.allocateDirect(MAX_PEOPLE * 36 * VERTEX_BYTES).order(ByteOrder.nativeOrder());
	private static int peopleVertices;
	private static final List<SkyLink.Actor> ACTORS = new ArrayList<>();

	private PtrOccluders() {
	}

	/** Called while LevelRenderer sets up its frame graph, just before the main pass is added. */
	public static void addPass(FrameGraphBuilder frame, LevelTargetBundle targets, CameraRenderState camera) {
		Vec3 cam = camera.pos;
		updateLevel(cam);
		fillPeople(cam);
		if (levelVertices == 0 && peopleVertices == 0) {
			return;
		}
		Matrix4f viewRotation = new Matrix4f(camera.viewRotationMatrix);
		FramePass pass = frame.addPass("ptrcraft_occluders");
		ResourceHandle<RenderTarget> main = pass.readsAndWrites(targets.main);
		targets.main = main;
		pass.executes(() -> draw(main.get(), viewRotation, cam));
	}

	private static void draw(RenderTarget target, Matrix4f viewRotation, Vec3 cam) {
		var device = RenderSystem.getDevice();
		GpuBufferSlice levelTransform = null;
		if (levelVertices > 0) {
			levelTransform = RenderSystem.getDynamicUniforms()
				.writeTransform(new Matrix4f(viewRotation).translate((float) (ox - cam.x), (float) (oy - cam.y), (float) (oz - cam.z)));
		}
		GpuBufferSlice peopleTransform = null;
		if (peopleVertices > 0) {
			if (people == null) {
				people = device.createBuffer(() -> "PTRCraft people occluders", GpuBuffer.USAGE_VERTEX | GpuBuffer.USAGE_COPY_DST, PEOPLE.capacity());
			}
			device.createCommandEncoder().writeToBuffer(people.slice(0, (long) peopleVertices * VERTEX_BYTES), PEOPLE.duplicate().position(0).limit(peopleVertices * VERTEX_BYTES));
			peopleTransform = RenderSystem.getDynamicUniforms().writeTransform(new Matrix4f(viewRotation));
		}
		try (RenderPass pass = device.createCommandEncoder()
				.createRenderPass(() -> "PTR occluders", target.getColorTextureView(), Optional.empty(), target.getDepthTextureView(), OptionalDouble.empty())) {
			pass.setPipeline(RenderSystem.getCompiledPipeline(PIPELINE));
			RenderSystem.bindDefaultUniforms(pass);
			if (levelTransform != null) {
				pass.setUniform("DynamicTransforms", levelTransform);
				pass.setVertexBuffer(0, level.slice());
				pass.draw(levelVertices, 1, 0, 0);
			}
			if (peopleTransform != null) {
				pass.setUniform("DynamicTransforms", peopleTransform);
				pass.setVertexBuffer(0, people.slice());
				pass.draw(peopleVertices, 1, 0, 0);
			}
		}
	}

	/** Takes a finished level mesh, and starts a new one when PTR's collision has changed. */
	private static void updateLevel(Vec3 cam) {
		if (building != null && building.isDone()) {
			try {
				Built b = building.join();
				if (level != null) {
					level.close();
					level = null;
				}
				levelVertices = 0;
				if (b.vertices() > 0) {
					level = RenderSystem.getDevice().createBuffer(() -> "PTRCraft level occluders", GpuBuffer.USAGE_VERTEX, b.data());
					levelVertices = b.vertices();
					ox = b.ox();
					oy = b.oy();
					oz = b.oz();
				}
				builtVersion = b.version();
				SkyCraft.LOG.info("PTRCraft: PTR's level hides Minecraft behind it: {} triangles", b.triangles());
			} catch (RuntimeException e) {
				if (buildErrors++ < 3) {
					SkyCraft.LOG.error("PTRCraft: building PTR occluders failed", e);
				}
			}
			building = null;
		}
		int version = SkyCollision.triangleVersion();
		if (building == null && version != builtVersion && System.nanoTime() - lastBuild > REBUILD_NANOS) {
			lastBuild = System.nanoTime();
			double bx = Math.floor(cam.x), by = Math.floor(cam.y), bz = Math.floor(cam.z);
			building = CompletableFuture.supplyAsync(() -> build(version, bx, by, bz), WORKER);
		}
	}

	/** Worker: every exact PTR triangle once (PTR sends one copy per 8-block region it touches). */
	private static Built build(int version, double bx, double by, double bz) {
		LongOpenHashSet seen = new LongOpenHashSet();
		FloatArrayList xyz = new FloatArrayList();
		SkyCollision.forEachTriangle(t -> {
			if (t.stairHelper || !seen.add(key(t))) {
				return; // stair ramps are only there to walk on
			}
			xyz.add((float) (t.ax - bx));
			xyz.add((float) (t.ay - by));
			xyz.add((float) (t.az - bz));
			xyz.add((float) (t.bx - bx));
			xyz.add((float) (t.by - by));
			xyz.add((float) (t.bz - bz));
			xyz.add((float) (t.cx - bx));
			xyz.add((float) (t.cy - by));
			xyz.add((float) (t.cz - bz));
		});
		ByteBuffer data = ByteBuffer.allocateDirect(Math.max(VERTEX_BYTES, xyz.size() * 4)).order(ByteOrder.nativeOrder());
		for (int i = 0; i < xyz.size(); i++) {
			data.putFloat(xyz.getFloat(i));
		}
		data.flip();
		return new Built(version, data, xyz.size() / 3, xyz.size() / 9, bx, by, bz);
	}

	private static long key(SkyTri t) {
		long h = 0x9E3779B97F4A7C15L;
		h = mix(h, t.ax);
		h = mix(h, t.ay);
		h = mix(h, t.az);
		h = mix(h, t.bx);
		h = mix(h, t.by);
		h = mix(h, t.bz);
		h = mix(h, t.cx);
		h = mix(h, t.cy);
		return mix(h, t.cz);
	}

	private static long mix(long h, double v) {
		h ^= Double.doubleToLongBits(v);
		h *= 0xBF58476D1CE4E5B9L;
		return h ^ (h >>> 31);
	}

	/** A box per standing PTR person near the camera (knocked-down and dead ones lie in odd shapes). */
	private static void fillPeople(Vec3 cam) {
		peopleVertices = 0;
		PEOPLE.clear();
		if (!SkyLink.readActors(ACTORS)) {
			return;
		}
		int n = 0;
		for (SkyLink.Actor a : ACTORS) {
			if (n >= MAX_PEOPLE || a.dead() || a.height() < 1.0F) {
				continue;
			}
			double dx = a.x() - cam.x, dz = a.z() - cam.z;
			if (dx * dx + dz * dz > PEOPLE_RANGE * PEOPLE_RANGE) {
				continue;
			}
			float r = a.width() * BODY_WIDTH * 0.5F;
			float x0 = (float) (dx - r), x1 = (float) (dx + r);
			float z0 = (float) (dz - r), z1 = (float) (dz + r);
			float y0 = (float) (a.y() - cam.y), y1 = y0 + a.height();
			box(x0, y0, z0, x1, y1, z1);
			n++;
		}
		peopleVertices = n * 36;
	}

	private static void box(float x0, float y0, float z0, float x1, float y1, float z1) {
		quad(x0, y0, z0, x1, y0, z0, x1, y1, z0, x0, y1, z0); // north
		quad(x0, y0, z1, x1, y0, z1, x1, y1, z1, x0, y1, z1); // south
		quad(x0, y0, z0, x0, y0, z1, x0, y1, z1, x0, y1, z0); // west
		quad(x1, y0, z0, x1, y0, z1, x1, y1, z1, x1, y1, z0); // east
		quad(x0, y0, z0, x1, y0, z0, x1, y0, z1, x0, y0, z1); // bottom
		quad(x0, y1, z0, x1, y1, z0, x1, y1, z1, x0, y1, z1); // top
	}

	private static void quad(float ax, float ay, float az, float bx, float by, float bz, float cx, float cy, float cz, float dx, float dy, float dz) {
		PEOPLE.putFloat(ax).putFloat(ay).putFloat(az).putFloat(bx).putFloat(by).putFloat(bz).putFloat(cx).putFloat(cy).putFloat(cz);
		PEOPLE.putFloat(cx).putFloat(cy).putFloat(cz).putFloat(dx).putFloat(dy).putFloat(dz).putFloat(ax).putFloat(ay).putFloat(az);
	}
}
