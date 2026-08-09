using System;
using System.IO;
using UnityEngine;
using PlaylistDirectoryManager = BeatSaberPlaylistsLib.PlaylistManager;

namespace PlaylistManager.Types
{
    /// <summary>
    /// A lightweight marker that lets a playlist directory occupy a cell in the
    /// game's native beatmap-level-pack grid.
    /// </summary>
    public sealed class FolderLevelPack : BeatmapLevelPack
    {
        private const string PackIdPrefix = "PlaylistManager.Folder:";

        public PlaylistDirectoryManager TargetManager { get; }
        public bool IsBack { get; }
        public bool ShowNameOnCover { get; }

        public FolderLevelPack(
            PlaylistDirectoryManager targetManager,
            Sprite coverImage,
            bool isBack = false,
            bool showNameOnCover = false)
            : base(
                CreatePackId(targetManager, isBack),
                CreateDisplayName(targetManager, isBack),
                CreateDisplayName(targetManager, isBack),
                coverImage,
                coverImage,
                PackBuyOption.DisableBuyOption,
                Array.Empty<BeatmapLevel>(),
                PlayerSensitivityFlag.Safe)
        {
            TargetManager = targetManager;
            IsBack = isBack;
            ShowNameOnCover = !isBack && showNameOnCover;
        }

        private static string CreateDisplayName(PlaylistDirectoryManager targetManager, bool isBack)
        {
            if (targetManager == null)
            {
                throw new ArgumentNullException(nameof(targetManager));
            }

            return isBack ? "Back" : Path.GetFileName(targetManager.PlaylistPath);
        }

        private static string CreatePackId(PlaylistDirectoryManager targetManager, bool isBack)
        {
            if (targetManager == null)
            {
                throw new ArgumentNullException(nameof(targetManager));
            }

            var normalizedPath = Path.GetFullPath(targetManager.PlaylistPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace('\\', '/');

            return $"{PackIdPrefix}{(isBack ? "Back" : "Open")}:{normalizedPath}";
        }
    }
}
