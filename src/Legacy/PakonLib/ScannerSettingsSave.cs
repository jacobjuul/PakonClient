using System;
using PakonLib.Enums;
using PakonLib.Models;

namespace PakonLib
{
    public class ScannerSettingsSave
    {
        private PictureIndex index;

        private SaveControl saveControl;

        private ScalingMethod scalingMethod;

        private MemoryFileFormat memoryFormat;

        public PictureIndex Index
        {
            get => index;
            set => index = value;
        }

        public SaveControl Control
        {
            get => saveControl;
            set => saveControl = value;
        }

        public ScalingMethod ScalingMethod
        {
            get => scalingMethod;
            set => scalingMethod = value;
        }

        public MemoryFileFormat MemoryFormat
        {
            get => memoryFormat;
            set => memoryFormat = value;
        }

        public event AddPictureToSaveGroup PictureAddedToSaveGroup;

        public void MoveRollToSaveGroup(Scanner scanner)
        {
            PictureCountSaveGroupResult saveGroupCounts = scanner.Images.GetPictureCountSaveGroup();
            if (saveGroupCounts.PictureCount > 0)
            {
                scanner.Images.PutPictureSelection(PictureIndex.All, PictureSelection.None, true);
            }

            PictureCountScanGroupResult scanGroupCounts = scanner.Images.GetPictureCountScanGroup(0);
            scanner.Images.MoveOldestRollToSaveGroup();
            for (int i = saveGroupCounts.PictureCount; i < saveGroupCounts.PictureCount + scanGroupCounts.PictureCount; i++)
            {
                PictureAddedToSaveGroup?.Invoke(i);
            }
        }

        public void SetPictureInfo(Scanner scanner, int indexValue, int newFrameNumber, string newFileName, string newDirectory, int newRotation, PictureSelection newSelectedHidden, IntBits info)
        {
            PictureInfo pictureInfo = scanner.ImageMetadata.GetPictureInfo3(indexValue);
            int frameNumber = pictureInfo.FrameNumber;
            string fileName = pictureInfo.FileName;
            string directory = pictureInfo.Directory;
            int rotation = pictureInfo.Rotation;
            PictureSelection selectedHidden = pictureInfo.SelectedHidden;
            if (info[0])
            {
                frameNumber = newFrameNumber;
            }
            if (info[1])
            {
                fileName = newFileName;
            }
            if (info[2])
            {
                directory = newDirectory;
            }
            if (info[3])
            {
                rotation = newRotation;
            }
            if (info[4])
            {
                selectedHidden = newSelectedHidden;
            }
            scanner.Images.PutPictureInfo(indexValue, frameNumber, fileName, directory, rotation, selectedHidden);
        }

        private BoundingRectangleMetrics FindBoundingRectangle(Scanner scanner, bool fourChannel)
        {
            PictureCountSaveGroupResult saveGroupCounts = scanner.Images.GetPictureCountSaveGroup();
            int pictureCount = saveGroupCounts.PictureCount;
            int startIndex;
            int endIndex;

            if (Index == PictureIndex.All || Index == PictureIndex.AllSelected)
            {
                startIndex = 0;
                endIndex = pictureCount;
            }
            else if (Index == PictureIndex.Current || Index == PictureIndex.First)
            {
                startIndex = (int)Index.NativeValue;
                endIndex = startIndex + 1;
            }
            else if (Index == PictureIndex.InsertPictureAtEnd)
            {
                throw new ArgumentException("Index not supported");
            }
            else
            {
                int rawIndex = (int)Index.NativeValue;
                if (pictureCount >= rawIndex)
                {
                    throw new ArgumentException("Index out of range");
                }

                startIndex = rawIndex;
                endIndex = startIndex + 1;
            }

            int boundingWidth = 0;
            int boundingHeight = 0;
            int bufferByteCount = 0;
            for (int currentIndex = startIndex; currentIndex < endIndex; currentIndex++)
            {
                bool include = true;
                if (Index == PictureIndex.AllSelected)
                {
                    PakonLib.Interfaces.ISavePictures3 saveInterface = scanner.ImageMetadata;
                    PictureInfo pictureInfo = saveInterface.GetPictureInfo3(currentIndex);
                    include = pictureInfo.SelectedHidden == PictureSelection.Selected;
                }

                if (include)
                {
                    PictureFramingInfo framingInfo = saveControl.HasFlag(SaveControl.UseLoResBuffer)
                        ? scanner.Images.GetPictureFramingUserInfoLowRes(currentIndex)
                        : scanner.Images.GetPictureFramingUserInfo(currentIndex);
                    int width = framingInfo.Right + 1 - framingInfo.Left;
                    int height = framingInfo.Bottom + 1 - framingInfo.Top;
                    int bufferSize = Global.BufferSize(width, height, memoryFormat, fourChannel);
                    if (boundingWidth < width)
                    {
                        boundingWidth = width;
                    }
                    if (boundingHeight < height)
                    {
                        boundingHeight = height;
                    }
                    if (bufferByteCount < bufferSize)
                    {
                        bufferByteCount = bufferSize;
                    }
                }
            }

            return new BoundingRectangleMetrics(boundingWidth, boundingHeight, bufferByteCount);
        }

        public void RenderToBuffer(Scanner scanner, ScannerSettings scannerSettings, bool fourChannel)
        {
            BoundingRectangleMetrics boundingMetrics = FindBoundingRectangle(scanner, fourChannel);
            if (boundingMetrics.BufferByteCount == 0)
            {
                throw new ArgumentException("No pictures to save");
            }

            scanner.Unsafe.MemoryFormat = MemoryFormat;
            scanner.Unsafe.Allocate(boundingMetrics.BufferByteCount);
            scanner.Unsafe.NextBuffer(scanner);
            scanner.Images.RenderToBuffer(scannerSettings.Type, index, saveControl, boundingMetrics.Width, boundingMetrics.Height, scalingMethod, memoryFormat, fourChannel);
        }
    }
}
