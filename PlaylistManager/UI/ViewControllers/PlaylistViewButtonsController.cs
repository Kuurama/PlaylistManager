using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using BeatSaberMarkupLanguage.Parser;
using HMUI;
using PlaylistManager.Interfaces;
using PlaylistManager.Utilities;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using IPA.Loader;
using PlaylistManager.Downloaders;
using SiraUtil.Zenject;
using Tweening;
using UnityEngine;
using Zenject;

namespace PlaylistManager.UI
{
    internal class PlaylistViewButtonsController : IInitializable, ITickable, IDisposable, INotifyPropertyChanged, ILevelCategoryUpdater, IParentManagerUpdater
    {
        private readonly PopupModalsController popupModalsController;
        private readonly TweeningManager uwuTweenyManager;
        private readonly PlaylistSequentialDownloader playlistDownloader;
        private readonly PlaylistDownloaderViewController playlistDownloaderViewController;
        private readonly PlaylistManagerFlowCoordinator playlistManagerFlowCoordinator;
        private readonly IVRInputModule vrInputModule;
        private readonly VRUIControls.VRPointer vrPointer;

        private readonly MainFlowCoordinator mainFlowCoordinator;
        private readonly AnnotatedBeatmapLevelCollectionsViewController annotatedBeatmapLevelCollectionsViewController;
        private readonly LevelFilteringNavigationController levelFilteringNavigationController;
        private readonly SelectLevelCategoryViewController selectLevelCategoryViewController;
        private readonly IconSegmentedControl levelCategorySegmentedControl;
        private readonly PluginMetadata pluginMetadata;
        private readonly BSMLParser bsmlParser;
        private readonly FoldersViewController foldersViewController;

        private BeatSaberPlaylistsLib.PlaylistManager parentManager;
        public event PropertyChangedEventHandler PropertyChanged;

        [UIComponent("root")]
        private readonly RectTransform rootTransform;

        [UIComponent("create-button")]
        private readonly ButtonIconImage createButton;

        [UIComponent("download-button")]
        private readonly ButtonIconImage downloadButton;

        [UIComponent("delete-folder-button")]
        private readonly ButtonIconImage deleteFolderButton;

        [UIComponent("flow-button")]
        private readonly ButtonIconImage flowButton;

        [UIComponent("create-menu")]
        private readonly ModalView createMenu;

        [UIComponent("create-menu")]
        private readonly RectTransform createMenuTransform;

        [UIComponent("create-options")]
        private readonly CustomListTableData createOptionsTableData;

        private UnityEngine.UI.Image downloadButtonImage;
        private Color downloadButtonImageColor;
        private Sprite createButtonSprite;
        private Sprite downloadButtonSprite;
        private Sprite deleteFolderButtonSprite;

        [UIComponent("queue-modal")]
        private readonly ModalView queueModal;

        [UIComponent("queue-modal")]
        private readonly RectTransform queueModalTransform;

        private Vector3 queueModalPosition;
        private Vector2? buttonGridOrigin;
        private bool triggerWasDown;

        [UIParams]
        private readonly BSMLParserParams parserParams;

        public PlaylistViewButtonsController(PopupModalsController popupModalsController, TimeTweeningManager uwuTweenyManager, PlaylistSequentialDownloader playlistDownloader, PlaylistDownloaderViewController playlistDownloaderViewController,
            MainFlowCoordinator mainFlowCoordinator, PlaylistManagerFlowCoordinator playlistManagerFlowCoordinator, AnnotatedBeatmapLevelCollectionsViewController annotatedBeatmapLevelCollectionsViewController,
            LevelFilteringNavigationController levelFilteringNavigationController, SelectLevelCategoryViewController selectLevelCategoryViewController, UBinder<Plugin, PluginMetadata> pluginMetadata, BSMLParser bsmlParser,
            IVRInputModule vrInputModule, [InjectOptional] FoldersViewController foldersViewController)
        {
            this.popupModalsController = popupModalsController;
            this.uwuTweenyManager = uwuTweenyManager;
            this.playlistDownloader = playlistDownloader;
            this.playlistDownloaderViewController = playlistDownloaderViewController;

            this.mainFlowCoordinator = mainFlowCoordinator;
            this.playlistManagerFlowCoordinator = playlistManagerFlowCoordinator;
            this.vrInputModule = vrInputModule;
            vrPointer = (vrInputModule as VRUIControls.VRInputModule)?._vrPointer;
            this.annotatedBeatmapLevelCollectionsViewController = annotatedBeatmapLevelCollectionsViewController;
            this.levelFilteringNavigationController = levelFilteringNavigationController;
            this.selectLevelCategoryViewController = selectLevelCategoryViewController;
            levelCategorySegmentedControl = selectLevelCategoryViewController._levelFilterCategoryIconSegmentedControl;
            this.pluginMetadata = pluginMetadata.Value;
            this.bsmlParser = bsmlParser;
            this.foldersViewController = foldersViewController;
        }

        public void Initialize()
        {
            bsmlParser.Parse(BeatSaberMarkupLanguage.Utilities.GetResourceContent(pluginMetadata.Assembly, "PlaylistManager.UI.Views.PlaylistViewButtons.bsml"), annotatedBeatmapLevelCollectionsViewController.gameObject, this);
            playlistDownloader.QueueUpdatedEvent += DownloadQueueUpdated;
            playlistDownloader.PopupEvent += TweenButton;
            vrInputModule.onProcessMousePressEvent += HandleGlobalPointerPress;
        }

        public void Dispose()
        {
            playlistDownloader.QueueUpdatedEvent -= DownloadQueueUpdated;
            playlistDownloader.PopupEvent -= TweenButton;
            vrInputModule.onProcessMousePressEvent -= HandleGlobalPointerPress;
            DestroyRuntimeSprite(createButtonSprite);
            DestroyRuntimeSprite(downloadButtonSprite);
            DestroyRuntimeSprite(deleteFolderButtonSprite);
        }

        public void Tick()
        {
            if (vrPointer == null)
            {
                return;
            }

            var triggerIsDown = vrPointer.lastSelectedVrController?.triggerValue >= 0.9f;
            var triggerWasPressed = triggerIsDown && !triggerWasDown;
            triggerWasDown = triggerIsDown;

            if (triggerWasPressed)
            {
                CloseCreateMenuUnlessPointerIsInside(vrPointer.pointingOver);
            }
        }

        private void DownloadQueueUpdated()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(QueueInteractable)));
            UpdateDownloadButtonAppearance();
        }

        private void TweenButton()
        {
            if (downloadButtonImage == null)
            {
                return;
            }

            uwuTweenyManager.KillAllTweens(downloadButtonImage);
            if (playlistDownloader.PendingPopup != null)
            {
                var tween = new FloatTween(0.35f, 0.6f, val =>
                {
                    downloadButtonImage.color = new Color(val, val, val);
                }, 0.75f, EaseType.InOutBack);
                uwuTweenyManager.AddTween(tween, downloadButtonImage);
                tween.onCompleted = delegate () { TweenButton(); };
            }
            else
            {
                UpdateDownloadButtonAppearance();
            }
        }

        private void UpdateDownloadButtonAppearance()
        {
            if (downloadButtonImage == null)
            {
                return;
            }

            downloadButtonImage.color = new Color(
                downloadButtonImageColor.r,
                downloadButtonImageColor.g,
                downloadButtonImageColor.b,
                QueueInteractable ? 1f : 0.25f);
        }

        public void LevelCategoryUpdated(SelectLevelCategoryViewController.LevelCategory levelCategory, bool viewControllerActivated)
        {
            if (rootTransform != null)
            {
                if (levelCategory == SelectLevelCategoryViewController.LevelCategory.CustomSongs)
                {
                    rootTransform.gameObject.SetActive(true);
                    PositionButtons();
                }
                else
                {
                    parserParams?.EmitEvent("close-create-menu");
                    rootTransform.gameObject.SetActive(false);
                }
            }
        }

        public void ParentManagerUpdated(BeatSaberPlaylistsLib.PlaylistManager parentManager)
        {
            this.parentManager = parentManager;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DeleteFolderInteractable)));
        }

        [UIAction("#post-parse")]
        private void PostParse()
        {
            queueModalPosition = queueModalTransform.localPosition;

            var buttonScale = new Vector3(0.36f, 0.36f, 1f);
            createButton.transform.localScale = buttonScale;
            downloadButton.transform.localScale = buttonScale;
            deleteFolderButton.transform.localScale = buttonScale;
            flowButton.transform.localScale = buttonScale;

            createButtonSprite = CreatePlusIcon();
            downloadButtonSprite = CreateDownloadIcon();
            deleteFolderButtonSprite = CreateTrashIcon();
            SetButtonIcon(createButton, createButtonSprite);
            SetButtonIcon(downloadButton, downloadButtonSprite);
            SetButtonIcon(deleteFolderButton, deleteFolderButtonSprite);

            createMenu._animateParentCanvas = false;
            createOptionsTableData.Data.Add(new CustomListTableData.CustomCellInfo("Playlist"));
            createOptionsTableData.Data.Add(new CustomListTableData.CustomCellInfo("Folder"));
            createOptionsTableData.TableView.ReloadData();

            downloadButtonImage = downloadButton.Image;
            downloadButtonImageColor = downloadButtonImage.color;
            UpdateDownloadButtonAppearance();

            var icon = flowButton.Image as ImageView;
            icon._skew = 0.18f;
            PositionButtons();
        }

        #region Create Playlist or Folder

        [UIAction("create-click")]
        private void CreateClicked()
        {
            PositionCreateMenu();
            parserParams.EmitEvent("close-create-menu");
            parserParams.EmitEvent("open-create-menu");
        }

        [UIAction("select-create-option")]
        private void SelectCreateOption(TableView tableView, int index)
        {
            if (index is < 0 or > 1)
            {
                return;
            }

            popupModalsController.ShowKeyboard(rootTransform, index == 0 ? CreatePlaylist : CreateFolder);
            tableView.ClearSelection();
            parserParams.EmitEvent("close-create-menu");
        }

        private void CreatePlaylist(string playlistName)
        {
            if (string.IsNullOrWhiteSpace(playlistName))
            {
                return;
            }

            var playlist = PlaylistLibUtils.CreatePlaylistWithConfig(playlistName, parentManager ?? BeatSaberPlaylistsLib.PlaylistManager.DefaultManager);
            popupModalsController.ShowYesNoModal(rootTransform, $"Successfully created {playlist.Title}", () =>
            {
                // In case the category isn't already playlists which it shouldn't be
                levelCategorySegmentedControl.SelectCellWithNumber(1);
                selectLevelCategoryViewController.LevelFilterCategoryIconSegmentedControlDidSelectCell(levelCategorySegmentedControl, 1);
                levelFilteringNavigationController.SelectAnnotatedBeatmapLevelCollection(playlist.PlaylistLevelPack);
            }, "Go to playlist", "Dismiss");
        }

        private void CreateFolder(string folderName)
        {
            folderName = SanitizeFolderName(folderName);
            if (string.IsNullOrWhiteSpace(folderName))
            {
                return;
            }

            var manager = foldersViewController?.CurrentParentManager
                ?? parentManager
                ?? BeatSaberPlaylistsLib.PlaylistManager.DefaultManager;
            var existingChildren = manager.GetChildManagers().ToArray();

            try
            {
                var childManager = manager.CreateChildManager(folderName);
                var alreadyExisted = existingChildren.Any(existingChild =>
                    ReferenceEquals(existingChild, childManager)
                    || string.Equals(
                        Path.GetFullPath(existingChild.PlaylistPath),
                        Path.GetFullPath(childManager.PlaylistPath),
                        StringComparison.OrdinalIgnoreCase));

                if (alreadyExisted)
                {
                    popupModalsController.ShowOkModal(rootTransform, $"\"{folderName}\" already exists! Please use a different name.", null);
                    return;
                }

                PlaylistLibUtils.playlistManager.RequestRefresh("PlaylistManager (plugin)");
                if (ReferenceEquals(manager, foldersViewController?.CurrentParentManager))
                {
                    foldersViewController.Refresh();
                }
            }
            catch (Exception exception)
            {
                Plugin.Log.Critical($"An exception was thrown while creating the playlist folder '{folderName}': {exception}");
                popupModalsController.ShowOkModal(rootTransform, "Error: Folder cannot be created.", null);
            }
        }

        private static string SanitizeFolderName(string folderName)
            => folderName?.Trim().Replace("/", "").Replace("\\", "").Replace(".", "");

        private void PositionCreateMenu()
        {
            Canvas.ForceUpdateCanvases();
            var createButtonCenter = GetButtonVisualCenter(createButton.GetComponent<UnityEngine.UI.Button>());
            var targetCenter = new Vector3(createButtonCenter.x - 12.5f, createButtonCenter.y - 3.425f, 0f);
            createMenuTransform.position = rootTransform.TransformPoint(targetCenter);
        }

        private void HandleGlobalPointerPress(GameObject currentOverGo)
            => CloseCreateMenuUnlessPointerIsInside(currentOverGo);

        private void CloseCreateMenuUnlessPointerIsInside(GameObject currentOverGo)
        {
            if (createMenu == null || !createMenu.gameObject.activeInHierarchy)
            {
                return;
            }

            if (currentOverGo != null && currentOverGo.transform.IsChildOf(createMenuTransform))
            {
                return;
            }

            parserParams.EmitEvent("close-create-menu");
        }

        #endregion

        #region Delete Folder

        [UIValue("delete-folder-interactable")]
        private bool DeleteFolderInteractable => foldersViewController?.CanDeleteCurrentFolder == true;

        [UIAction("delete-folder-click")]
        private void DeleteFolderClicked()
        {
            var folderManager = foldersViewController?.CurrentParentManager;
            if (folderManager?.Parent == null)
            {
                return;
            }

            var folderName = System.IO.Path.GetFileName(folderManager.PlaylistPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
            popupModalsController.ShowYesNoModal(
                rootTransform,
                $"Delete the folder \"{folderName}\" and everything inside it?",
                () => DeleteFolder(folderManager),
                "Delete Folder",
                "Cancel");
        }

        private void PositionButtons()
        {
            if (createButton == null || downloadButton == null || deleteFolderButton == null || flowButton == null)
            {
                return;
            }

            const float horizontalStep = 7.7f;
            const float verticalStep = 6.85f;
            const float lowerRowHorizontalOffset = -1.2f;
            Canvas.ForceUpdateCanvases();
            buttonGridOrigin ??= GetButtonVisualCenter(flowButton.GetComponent<UnityEngine.UI.Button>());
            var gridOrigin = buttonGridOrigin.Value;

            MoveButtonVisualCenter(flowButton.GetComponent<UnityEngine.UI.Button>(), gridOrigin + new Vector2(lowerRowHorizontalOffset, 0f));
            MoveButtonVisualCenter(deleteFolderButton.GetComponent<UnityEngine.UI.Button>(), gridOrigin + new Vector2(-horizontalStep + lowerRowHorizontalOffset, 0f));
            MoveButtonVisualCenter(downloadButton.GetComponent<UnityEngine.UI.Button>(), gridOrigin + new Vector2(0f, verticalStep));
            MoveButtonVisualCenter(createButton.GetComponent<UnityEngine.UI.Button>(), gridOrigin + new Vector2(-horizontalStep, verticalStep));
        }

        private void MoveButtonVisualCenter(UnityEngine.UI.Button button, Vector2 targetCenter)
        {
            var currentCenter = GetButtonVisualCenter(button);
            button.transform.localPosition += new Vector3(targetCenter.x - currentCenter.x, targetCenter.y - currentCenter.y, 0f);
        }

        private Vector2 GetButtonVisualCenter(UnityEngine.UI.Button button)
        {
            var background = button.transform.Find("BG") as RectTransform
                ?? GetSpriteSwapBackground(button)
                ?? button.targetGraphic?.rectTransform
                ?? (RectTransform)button.transform;
            var corners = new Vector3[4];
            background.GetWorldCorners(corners);
            var center = rootTransform.InverseTransformPoint((corners[0] + corners[2]) * 0.5f);
            return new Vector2(center.x, center.y);
        }

        private static RectTransform GetSpriteSwapBackground(UnityEngine.UI.Button button)
        {
            var spriteSwap = button.GetComponent<ButtonSpriteSwap>();
            if (spriteSwap?._images == null)
            {
                return null;
            }

            foreach (var image in spriteSwap._images)
            {
                if (image != null)
                {
                    return image.rectTransform;
                }
            }

            return null;
        }

        private static void SetButtonIcon(ButtonIconImage button, Sprite sprite)
        {
            button.Image.sprite = sprite;
            button.Image.preserveAspect = true;
        }

        private static Sprite CreatePlusIcon()
        {
            return CreateRuntimeIcon("PlaylistManagerPlusIcon", pixels =>
            {
                FillRect(pixels, 28, 12, 8, 40);
                FillRect(pixels, 12, 28, 40, 8);
            });
        }

        private static Sprite CreateDownloadIcon()
        {
            return CreateRuntimeIcon("PlaylistManagerDownloadIcon", pixels =>
            {
                FillRect(pixels, 28, 25, 8, 29);
                for (var y = 0; y < 17; y++)
                {
                    var halfWidth = 4 + y / 2;
                    FillRect(pixels, 32 - halfWidth, 13 + y, halfWidth * 2, 1);
                }

                FillRect(pixels, 12, 8, 40, 6);
                FillRect(pixels, 12, 8, 6, 12);
                FillRect(pixels, 46, 8, 6, 12);
            });
        }

        private static Sprite CreateTrashIcon()
        {
            return CreateRuntimeIcon("PlaylistManagerTrashIcon", pixels =>
            {
                FillRect(pixels, 13, 47, 38, 6);
                FillRect(pixels, 25, 54, 14, 5);
                FillRect(pixels, 18, 15, 6, 31);
                FillRect(pixels, 40, 15, 6, 31);
                FillRect(pixels, 18, 10, 28, 6);
                FillRect(pixels, 28, 20, 4, 20);
                FillRect(pixels, 35, 20, 4, 20);
            });
        }

        private static Sprite CreateRuntimeIcon(string name, Action<Color32[]> drawIcon)
        {
            const int iconSize = 64;
            var pixels = new Color32[iconSize * iconSize];
            drawIcon(pixels);

            var texture = new Texture2D(iconSize, iconSize, TextureFormat.RGBA32, false)
            {
                name = name + "Texture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            var sprite = Sprite.Create(texture, new Rect(0f, 0f, iconSize, iconSize), new Vector2(0.5f, 0.5f), iconSize / 10f);
            sprite.name = name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static void FillRect(Color32[] pixels, int x, int y, int width, int height)
        {
            const int iconSize = 64;
            var white = new Color32(255, 255, 255, 255);
            for (var row = y; row < y + height; row++)
            {
                for (var column = x; column < x + width; column++)
                {
                    pixels[row * iconSize + column] = white;
                }
            }
        }

        private static void DestroyRuntimeSprite(Sprite sprite)
        {
            if (sprite == null)
            {
                return;
            }

            var texture = sprite.texture;
            UnityEngine.Object.Destroy(sprite);
            UnityEngine.Object.Destroy(texture);
        }

        private void DeleteFolder(BeatSaberPlaylistsLib.PlaylistManager folderManager)
        {
            try
            {
                foldersViewController.DeleteFolder(folderManager);
            }
            catch (Exception exception)
            {
                Plugin.Log.Critical($"An exception was thrown while deleting the playlist folder '{folderManager?.PlaylistPath}': {exception}");
                popupModalsController.ShowOkModal(rootTransform, "Error: Folder cannot be deleted.", null);
            }
        }

        #endregion

        #region Download Queue

        [UIAction("queue-click")]
        private void ShowQueue()
        {
            queueModalTransform.localPosition = queueModalPosition;
            queueModal.Show(true, moveToCenter: false, finishedCallback: () =>
            {
                playlistDownloaderViewController.SetParent(queueModalTransform, new Vector3(0.75f, 0.75f, 1f));
            });
        }

        [UIValue("queue-interactable")]
        private bool QueueInteractable => PlaylistSequentialDownloader.downloadQueue.Count != 0;

        #endregion

        #region Settings

        [UIAction("flow-click")]
        private void ShowSettings()
        {
            playlistManagerFlowCoordinator.PresentFlowCoordinator(mainFlowCoordinator.YoungestChildFlowCoordinatorOrSelf());
        }

        #endregion
    }
}
