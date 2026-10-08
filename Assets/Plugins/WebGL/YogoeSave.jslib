mergeInto(LibraryManager.library, {
  // WebGL의 Application.persistentDataPath는 IndexedDB 위에 얹힌 IDBFS 가상 파일시스템이라,
  // C#의 File.Write만으로는 실제 브라우저 IndexedDB에 반영이 안 될 수 있다.
  // 저장/삭제 직후 이걸 호출해 메모리상의 FS 변경을 IndexedDB로 명시적으로 밀어준다.
  YogoeSyncFilesystem: function () {
    console.log("[DIAG] YogoeSyncFilesystem called. typeof FS=" + typeof FS +
      " FS.syncfs=" + (typeof FS !== "undefined" ? typeof FS.syncfs : "n/a"));
    if (typeof FS !== "undefined" && FS.syncfs) {
      FS.syncfs(false, function (err) {
        if (err) console.error("[Save] IndexedDB sync 실패", err);
        else console.log("[DIAG] YogoeSyncFilesystem FS.syncfs(false) callback OK, no err");
      });
    } else {
      console.warn("[DIAG] YogoeSyncFilesystem: FS not available, sync skipped entirely");
    }
  }
});
