using System;
using System.Diagnostics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Robust.Server.GameStates;

// Partial class for handling entity events (move, deletion, etc)
internal sealed partial class PvsSystem
{
    private void OnEntityMove(ref MoveEvent ev)
    {
        // Forge-Change-start: invalidate the global-override cache when an entity in (or moving in/out of) a cached
        // override subtree moves. Cheap O(1) hashset lookups guarded by Count != 0 to skip cost when no overrides exist.
        var movedUid = ev.Entity.Owner;
        var newParent = ev.Entity.Comp1.ParentUid;
        var oldParent = ev.OldPosition.EntityId;

        if (_globalOverrideSet.Count != 0 && (
                _globalOverrideSet.Contains(movedUid) ||
                _globalOverrideSet.Contains(newParent) ||
                _globalOverrideSet.Contains(oldParent)))
        {
            _pvsOverride.CacheDirty = true;
        }
        else if (_forceOverrideSet.Count != 0 && _forceOverrideSet.Contains(movedUid))
        {
            _pvsOverride.CacheDirty = true;
        }
        // Forge-Change-end

        UpdatePosition(ev.Entity.Owner, ev.Entity.Comp1, ev.Entity.Comp2, ev.OldPosition.EntityId);
    }

    private void OnTransformStartup(EntityUid uid, TransformComponent component, ref TransformStartupEvent args)
    {
        if (component.ParentUid == EntityUid.Invalid)
            return;

        // Forge-Change-start: a fresh entity born under a globally-overridden subtree must be added to the cache.
        if (_globalOverrideSet.Count != 0 && _globalOverrideSet.Contains(component.ParentUid))
            _pvsOverride.CacheDirty = true;
        // Forge-Change-end

        UpdatePosition(uid, component, MetaData(uid), EntityUid.Invalid);
    }

    private void OnEntityTerminating(ref EntityTerminatingEvent ev)
    {
        var meta = ev.Entity.Comp;

        _deletedEntities.Add(meta.NetEntity);
        _deletedTick.Add(_gameTiming.CurTick);
        RemoveEntityFromChunk(ev.Entity.Owner, meta);
    }

    private void UpdatePosition(EntityUid uid, TransformComponent xform, MetaDataComponent meta, EntityUid oldParent)
    {
        if (meta.EntityLifeStage >= EntityLifeStage.Terminating)
        {
            DebugTools.AssertNull(meta.LastPvsLocation);
            return;
        }

        // GridUid is only set after init.
        if (!xform._gridInitialized)
            _transform.InitializeGridUid(uid, xform);

        if (xform.GridUid == uid)
            return;

        DebugTools.Assert(!HasComp<MapGridComponent>(uid));
        DebugTools.Assert(!HasComp<MapComponent>(uid));

        if (oldParent != xform.ParentUid)
        {
            HandleParentChange(uid, xform, meta);
            return;
        }

        if (xform.ParentUid != xform.GridUid && xform.ParentUid != xform.MapUid)
            return;

        var location = new PvsChunkLocation(xform.ParentUid, GetChunkIndices(xform._localPosition));
        if (meta.LastPvsLocation == location)
            return;

        RemoveEntityFromChunk(uid, meta);
        AddEntityToChunk(uid, meta, location);
    }

    private void HandleParentChange(EntityUid uid, TransformComponent xform, MetaDataComponent meta)
    {
        RemoveEntityFromChunk(uid, meta);

        // moving to null space?
        if (xform.ParentUid == EntityUid.Invalid || xform.ParentUid == uid)
            return;

        var newRoot = (xform.GridUid ?? xform.MapUid);
        if (newRoot == null)
        {
            AssertNullspace(xform.ParentUid);
            return;
        }

        // If directly parented to the chunk, add as a direct child.
        if (xform.ParentUid == newRoot)
        {
            var location = new PvsChunkLocation(newRoot.Value, GetChunkIndices(xform._localPosition));
            AddEntityToChunk(uid, meta, location);
            return;
        }

        // Else, mark the new parent's last chunk as dirty. Null implies it is already dirty.
        if (MetaData(xform.ParentUid).LastPvsLocation is { } loc)
            DirtyChunk(loc);
    }

    [Conditional("DEBUG")]
    private void AssertNullspace(EntityUid uid)
    {
        if (uid == EntityUid.Invalid || !_xformQuery.TryGetComponent(uid, out var xform))
            return;

        DebugTools.AssertNull(xform.GridUid);
        DebugTools.AssertNull(xform.MapUid);
        AssertNullspace(xform.ParentUid);
    }

    internal void SyncMetadata(MetaDataComponent meta)
    {
        if (meta.PvsData == PvsIndex.Invalid)
            return;

        ref var ptr = ref _metadataMemory.GetRef(meta.PvsData.Index);
        ptr.VisMask = meta.VisibilityMask;
        ptr.LifeStage = meta.EntityLifeStage;
    }
}
