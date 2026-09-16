using System;
using PixelCore.Runtime.Serialization;

namespace PixelCore.Editor;

public static class PlayRoomRestore
{
    public static bool Run(EditorState state, Func<SceneData?> loadFromDisk, Action? afterRecharge = null)
    {
        var stash = state.TakePlayEntryStash();
        return stash != null
            ? Recharge(state, stash, keepDirty: true, afterRecharge)
            : Recharge(state, loadFromDisk(), keepDirty: false, afterRecharge);
    }

    public static bool Recharge(EditorState state, SceneData? data, bool keepDirty, Action? afterRecharge = null)
    {
        if (data == null || state.CurrentScene == null) return false;

        state.ClearSelection();
        state.CommandHistory.Clear();
        SceneSerializer.FromData(state.CurrentScene, data);
        state.CurrentScene.Name = data.Name;

        if (keepDirty) state.MarkDirty(); else state.ClearDirty();

        afterRecharge?.Invoke();
        return true;
    }
}
