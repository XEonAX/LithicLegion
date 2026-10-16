using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Recycles <see cref="PooledEffect"/> instances instead of instantiating and destroying them.
///
/// One pool is made per effect prefab, the first time that prefab is rented, under a "[Object Pool]"
/// object in the active scene. It is warmed to the prefab's own <see cref="PooledEffect.Prewarm"/>
/// count, so the first shot is not the one that pays for the Instantiate. An effect hands itself back
/// when its life runs out - nothing that spawns effects needs to hold a reference to a pool, and
/// nothing has to be wired up in the Inspector.
///
/// Rent, set the effect up, then activate it. A rented instance comes back inactive on purpose: a
/// forgotten one is invisible rather than a stale flash stuck at the muzzle.
/// </summary>
public static class ObjectPool
{
    const string RootName = "[Object Pool]";

    // A list rather than a map: there are a handful of effect prefabs, and matching on the reference
    // itself means no ID to keep in step with the prefab it stands for.
    static readonly List<Pool> Pools = new List<Pool>();
    static Transform root;

    /// <summary>The scene object every pooled instance lives under.</summary>
    static Transform Root
    {
        get
        {
            // Leaving play mode destroys the root, and the pools hanging off it die with it. Starting
            // the registry over is the only way to be sure we never hand out a destroyed instance.
            if (root == null)
            {
                Pools.Clear();
                root = new GameObject(RootName).transform;
            }

            return root;
        }
    }

    /// <summary>
    /// Takes an instance of <paramref name="prefab"/> out of its pool and parks it at the given pose.
    /// The instance is left inactive, and at the prefab's own scale - whatever the last life scaled it
    /// to is undone here.
    /// </summary>
    public static T Rent<T>(T prefab, Vector3 position, Quaternion rotation) where T : PooledEffect
    {
        if (prefab == null) return null;

        Pool pool = PoolFor(prefab);
        PooledEffect instance = pool.Take();

        Transform pose = instance.transform;
        pose.SetParent(pool.Root, false);
        pose.localScale = prefab.transform.localScale;
        pose.SetPositionAndRotation(position, rotation);

        instance.Source = prefab;
        return (T)instance;
    }

    /// <summary>Puts an instance back, ready to be rented again. Effects call this themselves.</summary>
    public static void Return(PooledEffect instance)
    {
        if (instance == null) return;

        instance.gameObject.SetActive(false);

        Pool pool = FindPool(instance.Source);
        if (pool == null)
        {
            // Not rented from a pool: either hand-placed in the scene, or its pool went away with the
            // scene that owned it. Nothing to recycle into, so let it go.
            Object.Destroy(instance.gameObject);
            return;
        }

        instance.transform.SetParent(pool.Root, false);
        pool.Give(instance);
    }

    /// <summary>
    /// The live pool a prefab already has, or null when it has none. A pool whose holder is gone
    /// counts as none, and is dropped on the way past.
    /// </summary>
    static Pool FindPool(PooledEffect prefab)
    {
        if (prefab == null) return null;

        for (int i = 0; i < Pools.Count; i++)
        {
            Pool pool = Pools[i];
            if (pool.Prefab != prefab) continue;

            // This project turns off domain reload on entering play mode, so these statics outlive the
            // scene they were built in and come back pointing at destroyed holders. Handing one out
            // would parent every effect to nothing, scattering them across the scene root.
            if (pool.Root == null)
            {
                Pools.RemoveAt(i);
                return null;
            }

            return pool;
        }

        return null;
    }

    /// <summary>The pool for a prefab, built and warmed on first use.</summary>
    static Pool PoolFor(PooledEffect prefab)
    {
        Pool existing = FindPool(prefab);
        if (existing != null) return existing;

        Transform sceneRoot = Root;     // read first: this is what rebuilds a stale registry

        var holder = new GameObject($"{prefab.name} (Pool)");
        holder.transform.SetParent(sceneRoot, false);

        var created = new Pool(prefab, holder.transform);
        Pools.Add(created);
        created.Warm();
        return created;
    }

    /// <summary>One prefab's worth of idle instances, and the object they sit under.</summary>
    sealed class Pool
    {
        readonly Stack<PooledEffect> idle = new Stack<PooledEffect>();

        public Pool(PooledEffect prefab, Transform root)
        {
            Prefab = prefab;
            Root = root;
        }

        public PooledEffect Prefab { get; }

        public Transform Root { get; }

        /// <summary>Builds the prewarm count up front, so the opening shots are not the slow ones.</summary>
        public void Warm()
        {
            int count = Mathf.Max(0, Prefab.Prewarm);
            for (int i = 0; i < count; i++) idle.Push(Create());
        }

        public PooledEffect Take()
        {
            // Instances can be destroyed behind the pool's back - a scene unload, or a hand-placed one
            // that hit Return. Skip the empties rather than handing out a null.
            while (idle.Count > 0)
            {
                PooledEffect instance = idle.Pop();
                if (instance != null) return instance;
            }

            return Create();
        }

        public void Give(PooledEffect instance)
        {
            idle.Push(instance);
        }

        PooledEffect Create()
        {
            PooledEffect instance = Object.Instantiate(Prefab, Root);
            instance.name = Prefab.name;
            instance.Source = Prefab;
            instance.gameObject.SetActive(false);
            return instance;
        }
    }
}
