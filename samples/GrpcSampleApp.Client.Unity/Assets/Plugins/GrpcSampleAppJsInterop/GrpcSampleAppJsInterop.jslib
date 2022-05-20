mergeInto(LibraryManager.library, {
  GrpcSampleAppJsInterop_GetCurrentLocation: function () {
    var returnStr = window.location.href;
    var bufferSize = lengthBytesUTF8(returnStr) + 1;
    var buffer = _malloc(bufferSize);
    stringToUTF8(returnStr, buffer, bufferSize);
    return buffer;
  },
});