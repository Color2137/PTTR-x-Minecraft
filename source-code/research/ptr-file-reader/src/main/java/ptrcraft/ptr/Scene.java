package ptrcraft.ptr;

import ptrcraft.ptr.unity.AssetEnv;
import ptrcraft.ptr.unity.SerializedFile;
import ptrcraft.ptr.unity.UObj;

import java.io.IOException;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/** The GameObject / Transform tree of one Unity serialized file, with world matrices and script names. */
public final class Scene {
	public final class Go {
		public final long id;
		public final String name;
		public final boolean activeSelf;
		public final List<Long> components = new ArrayList<>();
		public Go parent;
		public final List<Go> children = new ArrayList<>();
		public long transformId;
		float[] local = Mat.identity();
		private float[] world;

		Go(long id, String name, boolean active) {
			this.id = id;
			this.name = name;
			this.activeSelf = active;
		}

		public boolean activeInHierarchy() {
			for (Go g = this; g != null; g = g.parent) if (!g.activeSelf) return false;
			return true;
		}

		public float[] world() {
			if (world == null) world = parent == null ? local : Mat.mul(parent.world(), local);
			return world;
		}

		public float[] local() {
			return local;
		}

		public Go root() {
			Go g = this;
			while (g.parent != null) g = g.parent;
			return g;
		}

		public boolean under(Go ancestor) {
			for (Go g = this; g != null; g = g.parent) if (g == ancestor) return true;
			return false;
		}

		/** First component of this Unity type ("MeshFilter", "BoxCollider"...), or -1. */
		public long component(String type) {
			for (long c : components) if (type.equals(file.typeName(c))) return c;
			return -1;
		}

		public List<Long> componentsOf(String type) {
			List<Long> out = new ArrayList<>();
			for (long c : components) if (type.equals(file.typeName(c))) out.add(c);
			return out;
		}

		/** The MonoBehaviour whose script class is this name, or -1. */
		public long script(String className) throws IOException {
			for (long c : components) if ("MonoBehaviour".equals(file.typeName(c)) && className.equals(scriptName(c))) return c;
			return -1;
		}

		public Go find(String childName) {
			for (Go c : children) if (c.name.equals(childName)) return c;
			for (Go c : children) {
				Go f = c.find(childName);
				if (f != null) return f;
			}
			return null;
		}

		public String path() {
			return parent == null ? name : parent.path() + "/" + name;
		}
	}

	public final SerializedFile file;
	public final AssetEnv env;
	public final Map<Long, Go> gameObjects = new HashMap<>();
	public final Map<Long, Go> byTransform = new HashMap<>();
	public final List<Go> roots = new ArrayList<>();
	private final Map<String, String> scriptNames = new HashMap<>();

	public Scene(AssetEnv env, SerializedFile file) throws IOException {
		this.env = env;
		this.file = file;
		for (long id : file.objects.keySet()) {
			if (!"GameObject".equals(file.typeName(id))) continue;
			UObj g = file.read(id);
			Go go = new Go(id, g.s("m_Name"), g.b("m_IsActive"));
			for (UObj c : g.objs("m_Component")) go.components.add(c.ptr("component").pathId());
			gameObjects.put(id, go);
		}
		Map<Long, Long> parentOf = new HashMap<>();
		for (long id : file.objects.keySet()) {
			String t = file.typeName(id);
			if (!"Transform".equals(t) && !"RectTransform".equals(t)) continue;
			UObj tr = file.read(id);
			Go go = gameObjects.get(tr.ptr("m_GameObject").pathId());
			if (go == null) continue;
			go.transformId = id;
			go.local = Mat.trs(tr.vec3("m_LocalPosition"), tr.quat("m_LocalRotation"), tr.vec3("m_LocalScale"));
			byTransform.put(id, go);
			parentOf.put(id, tr.ptr("m_Father").pathId());
		}
		for (Map.Entry<Long, Long> e : parentOf.entrySet()) {
			Go go = byTransform.get(e.getKey());
			Go parent = byTransform.get(e.getValue());
			if (parent != null) {
				go.parent = parent;
				parent.children.add(go);
			} else roots.add(go);
		}
	}

	public String scriptName(long monoBehaviour) throws IOException {
		UObj mb = file.read(monoBehaviour);
		UObj.Ptr sp = mb.ptr("m_Script");
		String key = sp.fileId() + ":" + sp.pathId();
		String n = scriptNames.get(key);
		if (n == null) {
			AssetEnv.Ref r = env.resolve(file, sp);
			n = r == null ? "" : r.read().s("m_ClassName");
			scriptNames.put(key, n);
		}
		return n;
	}

	/** Every GameObject holding a MonoBehaviour of this script class. */
	public List<Go> withScript(String className) throws IOException {
		List<Go> out = new ArrayList<>();
		for (Go go : gameObjects.values()) if (go.script(className) >= 0) out.add(go);
		return out;
	}

	public Go root(String name) {
		for (Go g : roots) if (g.name.equals(name)) return g;
		return null;
	}
}
