﻿using HeicConverter.Data;
using ImageMagick;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Streams;
using Windows.UI.WebUI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace HeicConverter
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private const string SAVE_FOLDER_ACCESS_TOKEN = "SAVE_FOLDER_ACCESS_TOKEN";
        private MainPageViewModel ViewModel;
        public MainPage()
        {
            this.InitializeComponent();
            ViewModel = new MainPageViewModel();
        }

        private async void ConvertBtn_Click(object sender, RoutedEventArgs e)
        {
            ConvertionInProgress.IsActive = true;
            ConvertionInProgress.Visibility = Visibility.Visible;
            ConvertBtn.IsEnabled = false;
            var saveFolderPicker = new Windows.Storage.Pickers.FolderPicker
            {
                ViewMode = Windows.Storage.Pickers.PickerViewMode.List,
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary
            };
            saveFolderPicker.FileTypeFilter.Add("*");

            var _fileAccess = await saveFolderPicker.PickSingleFolderAsync();
            if (_fileAccess == null)
            {
                return;
            }

            Utils.RememberStorageItem(_fileAccess, SAVE_FOLDER_ACCESS_TOKEN);
            StorageApplicationPermissions.FutureAccessList.AddOrReplace(SAVE_FOLDER_ACCESS_TOKEN, _fileAccess);

            Task.Run(() => processAllFiles());
        }

        private async Task processAllFiles()
        {
            List<Task> tasks = new List<Task>();
            foreach (FileListElement file in ViewModel.files)
            {
                if (file.Status != FileStatus.INVALID && file.Status != FileStatus.COMPLETED)
                {
                    tasks.Add(ProcessFile(file));
                }
            }
            await Task.WhenAll(tasks);
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => {
                ConvertionInProgress.IsActive = false;
                ConvertionInProgress.Visibility = Visibility.Collapsed;
                ConvertBtn.IsEnabled = true;
            });
        }

        private async Task ProcessFile(FileListElement fileElm)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                fileElm.Status = FileStatus.IN_PROGRESS;
            });
            MagickImage img = null;
            try
            {
                img = await ReadFile(fileElm);
                ConvertFile(img);
                bool result = await SaveFile(img, Path.GetFileNameWithoutExtension(fileElm.Path));
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    fileElm.Status = result ? FileStatus.COMPLETED : FileStatus.ERROR;
                    fileElm.TooltipMsg = "";
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{ex.Message}");
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    fileElm.Status = FileStatus.ERROR;
                    fileElm.TooltipMsg = ex.Message;
                });
            }
            finally
            {
                if (img != null)
                {
                    img.Dispose();
                }
            }
        }

        private async Task<MagickImage> ReadFile(FileListElement fileElm)
        {

            StorageFile file = await Utils.GetFileForToken(fileElm.Token);
            using (IRandomAccessStream fileStream = await file.OpenAsync(FileAccessMode.Read))
            {
                return new MagickImage(fileStream.AsStream());
            }
        }

        private void ConvertFile(MagickImage img)
        {
            // TODO
            img.Format = MagickFormat.Png;
        }

        private async Task<bool> SaveFile(MagickImage img, string fileName)
        {
            StorageFolder targetFolder = await Utils.GetFolderForToken(SAVE_FOLDER_ACCESS_TOKEN);
            StorageFile targetFile = await targetFolder.CreateFileAsync($"{fileName}.png", CreationCollisionOption.GenerateUniqueName);
            await FileIO.WriteBytesAsync(targetFile, img.ToByteArray());
            Windows.Storage.Provider.FileUpdateStatus status =
                await CachedFileManager.CompleteUpdatesAsync(targetFile);
            if (status == Windows.Storage.Provider.FileUpdateStatus.Complete)
            {
                Debug.WriteLine("File " + targetFile.Name + " was saved.");
                return true;
            }
            else
            {
                Debug.WriteLine("File " + targetFile.Name + " couldn't be saved.");
                return false;
            }
        }


        private async void Grid_Drop(object sender, DragEventArgs e)
        {
            Grid_DragLeave(sender, e);
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                AddCollectionToFiles(items);
            }
        }

        private void Grid_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Drop here to add to the list";
            e.DragUIOverride.IsGlyphVisible = true;
            e.DragUIOverride.IsContentVisible = true;
            e.DragUIOverride.IsCaptionVisible = true;
        }

        private void Grid_DragEnter(object sender, DragEventArgs e)
        {
            MainApp_Grid.Visibility = Visibility.Collapsed;
            DragDrop_Grid.Visibility = Visibility.Visible;
        }

        private void Grid_DragLeave(object sender, DragEventArgs e)
        {
            MainApp_Grid.Visibility = Visibility.Visible;
            DragDrop_Grid.Visibility = Visibility.Collapsed;
        }

        private void RemoveFileBtn_Click(object sender, RoutedEventArgs e)
        {
            FileListElement i = (FileListElement)((FrameworkElement)sender).DataContext;
            ViewModel.files?.Remove(i);
        }

        private void ClearAllBtn_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.files.Clear();
        }

        private async void AddToListBtn_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker()
            {
                ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail,
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary
            };
            picker.FileTypeFilter.Add(".heic");
            picker.FileTypeFilter.Add(".heif");

            IReadOnlyList<StorageFile> filesToAdd = await picker.PickMultipleFilesAsync();
            AddCollectionToFiles(filesToAdd);
        }

        private void AddCollectionToFiles(IEnumerable<IStorageItem> items)
        {
            if (!items.Any()) return;
            foreach (var item in items)
            {
                if (item is StorageFile)
                {
                    StorageFile file = (StorageFile)item;
                    string token = Utils.RememberStorageItem(file);
                    bool isValid = file.Name.ToLower().EndsWith("heic") || file.Name.ToLower().EndsWith("heif");
                    if (!ViewModel.files.Any(x => x.Path == file.Path))
                    {
                        ViewModel.files.Add(new FileListElement(file.Name, file.Path, isValid ? FileStatus.PENDING : FileStatus.INVALID, token));
                    }
                }
            }
        }
    }
}
