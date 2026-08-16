using System;
using System.Collections.Generic;
using BeatSaberPlaylistsLib.Types;
using PlaylistManager.Utilities;
using HMUI;
using PlaylistManager.Configuration;
using PlaylistManager.UI;
using PlaylistManager.Types;
using SiraUtil.Affinity;
using TMPro;
using UnityEngine;

/*
 * Original Author: Auros
 * Taken from PlaylistCore
 */

namespace PlaylistManager.AffinityPatches
{
    // TODO: Fix lag when loading covers.
    // TODO: Don't load covers that have no size.
    internal class LevelCollectionCellSetDataPatch : IAffinity, IDisposable
    {
        private readonly Dictionary<IPlaylist, AnnotatedBeatmapLevelCollectionCell> eventTable = new();
        private readonly Dictionary<AnnotatedBeatmapLevelCollectionCell, IPlaylist> cellPlaylists = new();
        private readonly Dictionary<AnnotatedBeatmapLevelCollectionCell, FolderNameOverlay> folderNameOverlays = new();
        private readonly HoverHintController hoverHintController;
        private readonly PlaylistUpdater playlistUpdater;

        public LevelCollectionCellSetDataPatch(HoverHintController hoverHintController, PlaylistUpdater playlistUpdater)
        {
            this.hoverHintController = hoverHintController;
            this.playlistUpdater = playlistUpdater;
        }

        [AffinityPatch(typeof(AnnotatedBeatmapLevelCollectionCell), nameof(AnnotatedBeatmapLevelCollectionCell.SetData))]
        private void Patch(AnnotatedBeatmapLevelCollectionCell __instance, ref BeatmapLevelPack beatmapLevelPack)
        {
            RemoveCellMapping(__instance);

            if (beatmapLevelPack is PlaylistLevelPack playlistLevelPack)
            {
                var playlist = playlistLevelPack.playlist;
                if (eventTable.TryGetValue(playlist, out var previousCell))
                {
                    cellPlaylists.Remove(previousCell);
                }

                eventTable[playlist] = __instance;
                cellPlaylists[__instance] = playlist;
                playlist.SpriteLoaded -= OnSpriteLoaded;
                playlist.SpriteLoaded += OnSpriteLoaded;
            }

            if (PluginConfig.Instance.PlaylistHoverHints)
            {
                var hoverHint = __instance.GetComponent<HoverHint>();

                if (hoverHint == null)
                {
                    hoverHint = __instance.gameObject.AddComponent<HoverHint>();
                    Accessors.HoverHintControllerAccessor(ref hoverHint) = hoverHintController;
                }

                hoverHint.text = beatmapLevelPack.packName;
            }
        }

        [AffinityPatch(typeof(AnnotatedBeatmapLevelCollectionCell), nameof(AnnotatedBeatmapLevelCollectionCell.SetData))]
        [AffinityPostfix]
        private void SetFolderInfo(AnnotatedBeatmapLevelCollectionCell __instance, BeatmapLevelPack beatmapLevelPack)
        {
            if (beatmapLevelPack is FolderLevelPack folderLevelPack)
            {
                __instance._infoText.text = folderLevelPack.IsBack
                    ? "←  Back"
                    : $"{folderLevelPack.packName}\nFolder";

                SetFolderNameOverlay(
                    __instance,
                    folderLevelPack.ShowNameOnCover ? folderLevelPack.packName : null);
                return;
            }

            SetFolderNameOverlay(__instance, null);
        }

        private void SetFolderNameOverlay(AnnotatedBeatmapLevelCollectionCell cell, string folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName))
            {
                if (folderNameOverlays.TryGetValue(cell, out var existingOverlay) && existingOverlay.Root != null)
                {
                    existingOverlay.Root.SetActive(false);
                }

                return;
            }

            if (!folderNameOverlays.TryGetValue(cell, out var overlay) || overlay.Root == null)
            {
                overlay = CreateFolderNameOverlay(cell);
                folderNameOverlays[cell] = overlay;
            }

            overlay.Text.text = folderName;
            overlay.Root.SetActive(true);
            overlay.Root.transform.SetAsLastSibling();
        }

        private static FolderNameOverlay CreateFolderNameOverlay(AnnotatedBeatmapLevelCollectionCell cell)
        {
            var root = new GameObject(
                "PlaylistManager Folder Name Overlay",
                typeof(RectTransform));
            var rootTransform = (RectTransform)root.transform;
            rootTransform.SetParent(cell._coverImage.rectTransform, false);
            rootTransform.anchorMin = Vector2.zero;
            rootTransform.anchorMax = new Vector2(1f, 0.34f);
            rootTransform.offsetMin = Vector2.zero;
            rootTransform.offsetMax = Vector2.zero;

            // Cloning the cell's own label preserves Beat Saber's font and curved-UI
            // material, so the overlay looks native and follows future font changes.
            var text = UnityEngine.Object.Instantiate(cell._infoText, rootTransform, false);
            text.name = "PlaylistManager Folder Name";
            text.text = string.Empty;
            text.color = Color.white;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.enableAutoSizing = true;
            text.fontSizeMin = 1.6f;
            text.fontSizeMax = 3.2f;
            text.raycastTarget = false;

            var textTransform = text.rectTransform;
            textTransform.anchorMin = Vector2.zero;
            textTransform.anchorMax = Vector2.one;
            textTransform.offsetMin = new Vector2(0.35f, 0.08f);
            textTransform.offsetMax = new Vector2(-0.35f, -0.08f);
            text.gameObject.SetActive(true);

            return new FolderNameOverlay(root, text);
        }

        private void OnSpriteLoaded(object sender, EventArgs e)
        {
            // TODO: Figure out why this doesn't seem to happen.
            if (sender is not IPlaylist playlist)
            {
                return;
            }

            playlist.SpriteLoaded -= OnSpriteLoaded;

            if (!eventTable.TryGetValue(playlist, out var tableCell))
            {
                return;
            }

            eventTable.Remove(playlist);
            cellPlaylists.Remove(tableCell);

            if (tableCell == null
                || tableCell._beatmapLevelPack is not PlaylistLevelPack currentPlaylistLevelPack
                || !ReferenceEquals(currentPlaylistLevelPack.playlist, playlist))
            {
                return;
            }

            tableCell._coverImage.sprite = playlist.SmallSprite;
            // TODO: Figure out why this needs to be done here and in UpdatePlaylist when switching covers. Worth noting that this event is invoked twice as well.
            playlistUpdater.RefreshAnnotatedBeatmapCollection(tableCell._beatmapLevelPack);
        }

        private void RemoveCellMapping(AnnotatedBeatmapLevelCollectionCell cell)
        {
            if (!cellPlaylists.TryGetValue(cell, out var previousPlaylist))
            {
                return;
            }

            cellPlaylists.Remove(cell);
            previousPlaylist.SpriteLoaded -= OnSpriteLoaded;
            if (eventTable.TryGetValue(previousPlaylist, out var mappedCell) && ReferenceEquals(mappedCell, cell))
            {
                eventTable.Remove(previousPlaylist);
            }
        }

        public void Dispose()
        {
            foreach (var playlist in eventTable.Keys)
            {
                playlist.SpriteLoaded -= OnSpriteLoaded;
            }

            eventTable.Clear();
            cellPlaylists.Clear();

            foreach (var overlay in folderNameOverlays.Values)
            {
                if (overlay.Root != null)
                {
                    UnityEngine.Object.Destroy(overlay.Root);
                }
            }

            folderNameOverlays.Clear();
        }

        private sealed class FolderNameOverlay
        {
            internal FolderNameOverlay(GameObject root, TextMeshProUGUI text)
            {
                Root = root;
                Text = text;
            }

            internal GameObject Root { get; }
            internal TextMeshProUGUI Text { get; }
        }
    }
}
