using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using PlaylistManager.Configuration;
using PlaylistManager.Utilities;

namespace PlaylistManager.HarmonyPatches
{
    [HarmonyPatch(typeof(LevelFilteringNavigationController), nameof(LevelFilteringNavigationController.ShowPacksInSecondChildController))]
    public class LevelFilteringNavigationController_ShowPacksInChildController
    {
        internal static void Prefix(LevelFilteringNavigationController __instance, ref IReadOnlyList<BeatmapLevelPack> beatmapLevelPacks)
        {
            // FolderNavigationPatches owns this list while folders are enabled. Preserve the
            // legacy flat view only when the user explicitly disables folder navigation.
            if (PluginConfig.Instance.FoldersDisabled
                && __instance._selectLevelCategoryViewController.selectedLevelCategory == SelectLevelCategoryViewController.LevelCategory.CustomSongs)
            {
                beatmapLevelPacks = beatmapLevelPacks.ToArray().AddRangeToArray(PlaylistLibUtils.TryGetAllPlaylistsAsLevelPacks());
            }
        }
    }
}
