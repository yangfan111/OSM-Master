﻿using System;
using System.Collections.Generic;
using OSMTrafficSim.BVH;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Experimental.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;

namespace OSMTrafficSim
{
    public class InstanceRenderingSystem : ComponentSystem
    {
        public Camera ActiveCamera;
        private NativeMultiHashMap<int, Entity> _batcher;
        private EntityArchetypeQuery _query;
        private NativeArray<float> _cullDistance;
        private List<InstanceRendererData> _renderData;
        private InstanceRenderer _renderer;

        protected override void OnCreateManager()
        {
            _query = new EntityArchetypeQuery()
            {
                Any = Array.Empty<ComponentType>(),
                None = Array.Empty<ComponentType>(),
                All = new ComponentType[] { typeof(InstanceRendererData), typeof(InstanceRendererProperty), typeof(LocalToWorld) }
            };

            _renderData = new List<InstanceRendererData>();
            EntityManager.GetAllUniqueSharedComponentData(_renderData);
            List<float> cullDistance = _renderData.ConvertAll( r => r.CullDistance);
            _cullDistance = new NativeArray<float>(cullDistance.ToArray(), Allocator.Persistent);
            _cullDistance.CopyFrom(cullDistance.ToArray());

            _batcher = new NativeMultiHashMap<int, Entity>(10000, Allocator.Persistent);

            _renderer = new InstanceRenderer(EntityManager);
        }

        protected override void OnDestroyManager()
        {
            _cullDistance.Dispose();
            _batcher.Dispose();
            _renderer.Dispose();
        }

        protected override void OnUpdate()
        {
        }

        public void Tick()
        {
            if (ActiveCamera == null || !_batcher.IsCreated) return;
            //share component id can only be visited by architypechunks, 
            //so we iterate over architypechunks here
            //https://github.com/Unity-Technologies/EntityComponentSystemSamples/blob/8f94d72d1fd9b8db896646d9d533055917dc265a/Documentation/reference/chunk_iteration.md
            _batcher.Clear();
            UnityEngine.Profiling.Profiler.BeginSample("gather chunks");
            NativeArray<ArchetypeChunk> chunks = EntityManager.CreateArchetypeChunkArray(_query, Allocator.TempJob);
            UnityEngine.Profiling.Profiler.EndSample();
            UnityEngine.Profiling.Profiler.BeginSample("start cull");
            var cullJob = new CullJob()
            {
                EntityType = GetArchetypeChunkEntityType(),
                Chunks = chunks,
                RenderTypes = GetArchetypeChunkSharedComponentType<InstanceRendererData>(),
                LocalToWorldType = GetArchetypeChunkComponentType<LocalToWorld>(true),
                Batcher = _batcher.ToConcurrent(),
                CamPos = ActiveCamera.transform.position,
                CullDistance = _cullDistance
            };
            var deps = cullJob.Schedule(chunks.Length, 1);
            deps.Complete();
            UnityEngine.Profiling.Profiler.EndSample();
            UnityEngine.Profiling.Profiler.BeginSample("start render");
            Render();
            UnityEngine.Profiling.Profiler.EndSample();
        }

        public void Render()
        {
            //_renderer.Clean(ActiveCamera);
            for (int i = 0; i < _renderData.Count; i++)
            {
                if (_renderData[i].Material && _renderData[i].Mesh)
                {
                    Entity ent;
                    NativeMultiHashMapIterator<int> iterator;
                    if (_batcher.TryGetFirstValue(i, out ent, out iterator))
                    {
                        InstanceRendererProperty prop = EntityManager.GetComponentData<InstanceRendererProperty>(ent);
                        _renderer.Init(_renderData[i], prop);
                        _renderer.Batch(ent);
                        while (_batcher.TryGetNextValue(out ent, ref iterator))
                        {
                            _renderer.Batch(ent);
                        }
                        _renderer.Final();
                    }
                }
            }
        }
        

    }
}
