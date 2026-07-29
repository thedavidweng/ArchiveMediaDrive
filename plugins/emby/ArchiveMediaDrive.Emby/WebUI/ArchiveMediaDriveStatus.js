define(["emby-button"], function () {
    return function (view) {
        function loadStatus() {
            var url = ApiClient.getUrl("/ArchiveMediaDrive/Status");
            ApiClient.getJSON(url).then(function (status) {
                view.querySelector("#PluginVersion").textContent = status.version || "-";
                view.querySelector("#HostVersion").textContent = status.hostVersion || "-";
                view.querySelector("#RuntimeStatus").textContent = status.runtimeStatus || "-";
                view.querySelector("#RcloneVersion").textContent = status.rcloneVersion || "-";
                view.querySelector("#RcloneHash").textContent = status.rcloneHash || "-";
                view.querySelector("#MountStatus").textContent = status.mountStatus || "-";
                view.querySelector("#MountPath").textContent = status.mountPath || "-";
                view.querySelector("#CacheUsage").textContent = status.cacheUsage ? (status.cacheUsage / 1024 / 1024).toFixed(2) + " MB" : "-";
                view.querySelector("#LastRefresh").textContent = status.lastRefresh ? new Date(status.lastRefresh).toLocaleString() : "-";
                view.querySelector("#SourceCount").textContent = status.sourceCount != null ? status.sourceCount : "-";
                view.querySelector("#ItemCount").textContent = status.itemCount != null ? status.itemCount : "-";
                view.querySelector("#LastError").textContent = status.lastError || "-";
            });
        }

        view.addEventListener("viewshow", function () {
            loadStatus();
        });

        view.querySelector("#DownloadDiagnostics").addEventListener("click", function () {
            window.location.href = ApiClient.getUrl("/ArchiveMediaDrive/Diagnostics");
        });
    };
});
