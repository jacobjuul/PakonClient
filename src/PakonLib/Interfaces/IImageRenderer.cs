using PakonLib.Enums;
using PakonLib.Models;

namespace PakonLib.Interfaces
{
    /// <summary>
    /// Renders framed scan images into files or caller-owned buffers.
    /// </summary>
    public interface IImageRenderer
    {
        PictureCountScanGroupResult GetPictureCountScanGroup(int rollIndex);
        void MoveOldestRollToSaveGroup();
        PictureCountSaveGroupResult GetPictureCountSaveGroup();
        void RenderToFile(PictureIndex index, SaveControl saveControl, int boundingWidth, int boundingHeight, ScalingMethod scalingMethod, FileFormat fileFormat, int compression, int dpi, int colorBits);
        void RegisterRenderBuffer(int byteStartPointer, int byteCount);
        void ClearRenderBuffers();
        void RenderToBuffer(ScannerType scannerType, PictureIndex index, SaveControl saveControl, int boundingWidth, int boundingHeight, ScalingMethod scalingMethod, MemoryFileFormat fileFormat, bool fourChannel);
        void CancelRender();
        void PutPictureInfo(int index, int frameNumber, string fileName, string directory, int rotation, PictureSelection selectedHidden);
        void PutPictureSelection(PictureIndex index, PictureSelection selectOrHidden, bool skipHidden);
        PictureFramingInfo GetPictureFramingUserInfo(int index);
        PictureFramingInfo GetPictureFramingUserInfoLowRes(int index);
    }
}
