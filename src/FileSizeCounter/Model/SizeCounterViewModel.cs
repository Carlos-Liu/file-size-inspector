﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using Microsoft.VisualBasic.FileIO;
using FileSizeCounter.Common;
using FileSizeCounter.Extensions;
using FileSizeCounter.MicroMvvm;
using Res;

namespace FileSizeCounter.Model
{
    /// <summary>
    /// The view model which encapsulates the business logics.
    /// </summary>
    public class SizeCounterViewModel : ObservableObject, IDataErrorInfo
    {
        private const double DefaultFilterSize = 1;
        private readonly IBusyIndicatorWindow _BusyWindow;

        public SizeCounterViewModel(IBusyIndicatorWindow busyIndicatorWindow)
        {
            TargetDirectory = @"C:\";
            SizeFilterValue = DefaultFilterSize.ToString(CultureInfo.InvariantCulture);
            HighlightElements = true;
            _BusyWindow = busyIndicatorWindow;
        }

        private string _SizeFilterValue;

        private double _FilterSize;

        public double FilterSize
        {
            get { return _FilterSize; }
            set
            {
                if (_FilterSize.AlmostEqualTo(value))
                {
                    return;
                }

                _FilterSize = value;

                if (ElementList.Count == 0)
                {
                    return;
                }

                RefreshElementsVisibilities();
            }
        }

        public IElement SelectedElement { get; set; }

        #region Data bindings

        private readonly ObservableCollection<IElement> _ElementList = new ObservableCollection<IElement>();
        private string _TargetDirectory;

        public ObservableCollection<IElement> ElementList
        {
            get { return _ElementList; }
        }

        /// <summary>
        ///   The root directory that will be processed to get the inside file/folder size
        /// </summary>
        public string TargetDirectory
        {
            get { return _TargetDirectory; }
            set
            {
                if (!_TargetDirectory.CompareOrdinal(value, true))
                {
                    _TargetDirectory = value;
                    RaisePropertyChanged();
                }
            }
        }

        public string SizeFilterValue
        {
            get { return _SizeFilterValue; }
            set
            {
                if (!_SizeFilterValue.CompareOrdinal(value))
                {
                    _SizeFilterValue = value;

                    if (string.IsNullOrWhiteSpace(value))
                        FilterSize = DefaultFilterSize;
                    else
                    {
                        double parsedValue;
                        bool succeeded = double.TryParse(value, out parsedValue);
                        if (succeeded)
                            FilterSize = parsedValue;
                    }
                }
            }
        }

        public bool HideSmallerElements
        {
            get { return _HideSmallerElements; }
            set
            {
                if (_HideSmallerElements != value)
                {
                    _HideSmallerElements = value;
                    RefreshElementsVisibilities();
                    RaisePropertyChanged();
                }
            }
        }

        public bool HighlightElements
        {
            get { return _HighlightElements; }
            set
            {
                if (_HighlightElements != value)
                {
                    _HighlightElements = value;
                    RefreshElementsVisibilities();
                    RaisePropertyChanged();
                }
            }
        }

        /// <summary>
        /// The process result message
        /// </summary>
        public string ProcessResult
        {
            get { return _ProcessResult; }
            set
            {
                if (!_ProcessResult.CompareOrdinal(value))
                {
                    _ProcessResult = value;
                    RaisePropertyChanged();
                }
            }
        }

        /// <summary>
        /// The detailed error message for the processing
        /// </summary>
        public string ProcessDetailedErrors
        {
            get { return _ProcessDetailedErrors; }
            set
            {
                _ProcessDetailedErrors = value;


                if (string.IsNullOrWhiteSpace(value))
                {
                    ProcessResult = Resources.Message_ParseResult_Succeeded;
                    ProcessResultIconFile = "Images/success.png";
                }
                else
                {
                    ProcessResult = Resources.Message_ParseResult_Failed;
                    ProcessResultIconFile = "Images/success-with-error.png";
                }

                RaisePropertyChanged();
            }
        }

        /// <summary>
        /// The icon file that represents the process result
        /// </summary>
        public string ProcessResultIconFile
        {
            get { return _ProcessResultIconFile; }
            set
            {
                if (!_ProcessResultIconFile.CompareOrdinal(value))
                {
                    _ProcessResultIconFile = value;
                    RaisePropertyChanged();
                }
            }
        }

        #region Delete Command

        private RelayCommand _DeleteCmd;

        /// <summary>
        /// Command for deleting the selected item
        /// </summary>
        public RelayCommand DeleteCmd
        {
            get
            {
                if (_DeleteCmd == null)
                    _DeleteCmd = new RelayCommand(OnDeleteSelectedItem, CanDelete);

                return _DeleteCmd;
            }
        }

        internal bool CanDelete()
        {
            return SelectedElement != null &&
                   SelectedElement.Parent != null;
        }

        internal void OnDeleteSelectedItem()
        {
            Debug.Assert(SelectedElement != null);

            var parentElement = SelectedElement.Parent as FolderElement;
            Debug.Assert(parentElement != null);

            try
            {
                // There will be the system deletion confirmation dialog pop up when specifying the UIOption.AllDialogs argument
                if (SelectedElement is FileElement)
                {
                    FileSystem.DeleteFile(SelectedElement.Name, UIOption.AllDialogs, RecycleOption.SendToRecycleBin);
                }
                else
                {
                    FileSystem.DeleteDirectory(SelectedElement.Name, UIOption.AllDialogs, RecycleOption.SendToRecycleBin);
                }

                // do this after the file/folder was removed from disk
                parentElement.Remove(SelectedElement);
            }
            catch (OperationCanceledException)
            {
                // The exception will occur if select 'No' in the deletion confirmation dialog, so just swallow the exception
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Resources.Message_Error_FailToDeletePrompt, SelectedElement.Name, ex.Message),
                  Resources.Message_ApplicationTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        #endregion

        #region Open in explorer Command

        private RelayCommand _OpenInExplorerCmd;

        public RelayCommand OpenInExplorerCmd
        {
            get
            {
                if (_OpenInExplorerCmd == null)
                    _OpenInExplorerCmd = new RelayCommand(OnOpenInExplorer);

                return _OpenInExplorerCmd;
            }
        }

        private void OnOpenInExplorer()
        {
            Debug.Assert(SelectedElement != null);

            Helper.OpenFolderAndSelectFile(SelectedElement.Name);
        }

        #endregion

        #region Start Inspect Command

        private RelayCommand _StartCommand;
        private string _ProcessResult;
        private string _ProcessDetailedErrors;
        private string _ProcessResultIconFile;
        private bool _HideSmallerElements;
        private bool _HighlightElements;

        /// <summary>
        ///   Command for the start action
        /// </summary>
        public RelayCommand StartCmd
        {
            get
            {
                if (_StartCommand == null)
                    _StartCommand = new RelayCommand(Start, CanStart);

                return _StartCommand;
            }
        }

        // If can start the process
        internal bool CanStart()
        {
            return !string.IsNullOrWhiteSpace(TargetDirectory) &&
                   Directory.Exists(TargetDirectory) &&
                   string.IsNullOrEmpty(Error);
        }

        private void Start()
        {
            ElementList.Clear();

            var message = Resources.Message_BusyIndicator_Title;

            var result = _BusyWindow.ExecuteAndWait(message, InspectDirectory);
            if (_BusyWindow.IsSuccessfullyExecuted == true)
            {
                ElementList.Add(result);
                result.IsExpanded = true;

                RefreshElementsVisibilities();
            }
            else
            {
                MessageBox.Show(
                  string.Format(Resources.Message_Error_ParsingDirectoryFailed, _BusyWindow.ExecutionException.Message),
                  Resources.Message_ApplicationTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private FolderElement InspectDirectory()
        {
            Stack<FolderElement> stack = new Stack<FolderElement>();
            var rootElement = new FolderElement(TargetDirectory);
            stack.Push(rootElement);
            var errors = new StringBuilder();

            while (stack.Count > 0)
            {
                try
                {
                    var currentFolderElement = stack.Pop();
                    var directoryName = currentFolderElement.Name;

                    var fileEntries = Directory.EnumerateFiles(directoryName);
                    foreach (var fileName in fileEntries)
                    {
                        var fileInfo = new FileInfo(fileName);
                        var fileElement = new FileElement(fileName, fileInfo.Length);
                        currentFolderElement.Add(fileElement);

                        _BusyWindow.ShowCurrentInspectingElement(fileName);
                    }

                    var subDirectoryEntries = Directory.EnumerateDirectories(directoryName);
                    foreach (var subDirectory in subDirectoryEntries)
                    {
                        var folderElement = new FolderElement(subDirectory);
                        currentFolderElement.Add(folderElement);

                        _BusyWindow.ShowCurrentInspectingElement(subDirectory);

                        stack.Push(folderElement);
                    }

                }
                catch (UnauthorizedAccessException ex)
                {
                    errors.AppendLine(ex.Message);
                }
                catch (FileNotFoundException ex)
                {
                    errors.AppendLine(ex.Message);
                }
                catch (Exception ex)
                {
                    errors.AppendLine(ex.Message);
                }
            }

            ProcessDetailedErrors = errors.ToString();

            return rootElement;
        }

        #endregion


        #endregion

        private void RefreshElementsVisibilities()
        {
            if (ElementList.Count == 0)
            {
                return;
            }

            Stack<FolderElement> stack = new Stack<FolderElement>();
            stack.Push(ElementList[0] as FolderElement);

            while (stack.Count > 0)
            {
                var currentFolder = stack.Pop();
                foreach (var element in currentFolder.Children)
                {
                    // clear previous settings
                    element.ShouldBeHighlighted = false;
                    element.IsVisible = true;

                    // in bytes
                    if (element.Size >= (FilterSize * 1024 * 1024))
                    {
                        if (HighlightElements)
                        {
                            element.ShouldBeHighlighted = true;
                        }

                        if (element is FolderElement)
                        {
                            stack.Push(element as FolderElement);
                        }
                    }
                    else
                    {
                        if (HideSmallerElements)
                        {
                            element.IsVisible = false;
                        }
                    }
                }
            }
        }

        #region IDataErrorInfo Members

        public string Error
        {
            get
            {
                var error = this["TargetDirectory"];
                if (!string.IsNullOrEmpty(error))
                    return error;

                error = this["SizeFilterValue"];
                if (!string.IsNullOrEmpty(error))
                    return error;

                return string.Empty;
            }
        }

        public string this[string columnName]
        {
            get
            {
                switch (columnName)
                {
                    case "TargetDirectory":
                        return Validator.ValidateInspectDirectory(TargetDirectory);

                    case "SizeFilterValue":
                        return Validator.ValidateSizeFilterValue(SizeFilterValue);

                    default:
                        return string.Empty;
                }
            }
        }

        #endregion
    }
}