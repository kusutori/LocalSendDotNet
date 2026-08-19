using System.Net;

namespace LocalSendDotNet;

internal static class WebShareHtml
{
    public static string Render(string title, bool pinRequired) => $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{{WebUtility.HtmlEncode(title)}}</title>
        <style>
        :root { color-scheme: light dark; }
        body { font-family: system-ui, sans-serif; margin: 0 auto; padding: 24px; max-width: 40rem; }
        h1 { font-size: 1.5rem; margin: 0 0 16px; }
        a.file { display: flex; justify-content: space-between; gap: 16px; padding: 14px 0; border-bottom: 1px solid color-mix(in srgb, currentColor 16%, transparent); text-decoration: none; color: inherit; }
        .muted { opacity: .65; }
        .status { margin: 12px 0 20px; }
        </style>
        </head>
        <body>
        <h1>{{WebUtility.HtmlEncode(title)}}</h1>
        <p id="status" class="status muted"></p>
        <div id="files"></div>
        <script>
        const pinRequired = {{(pinRequired ? "true" : "false")}};
        function formatSize(bytes) {
          if (bytes < 1024) return bytes + ' B';
          if (bytes < 1048576) return (bytes / 1024).toFixed(1) + ' KB';
          return (bytes / 1048576).toFixed(1) + ' MB';
        }
        async function start() {
          const status = document.getElementById('status');
          const box = document.getElementById('files');
          let pin = '';
          if (pinRequired) {
            pin = prompt('PIN') || '';
          }
          status.textContent = '…';
          const response = await fetch('/api/localsend/v2/prepare-download?pin=' + encodeURIComponent(pin), { method: 'POST' });
          if (response.status === 401) { status.textContent = 'PIN'; return; }
          if (response.status === 429) { status.textContent = 'PIN'; return; }
          if (response.status === 403) { status.textContent = 'Denied'; return; }
          if (!response.ok) { status.textContent = 'Error ' + response.status; return; }
          const data = await response.json();
          status.textContent = '';
          for (const file of data.files) {
            const link = document.createElement('a');
            link.className = 'file';
            link.href = '/api/localsend/v2/download?sessionId=' + encodeURIComponent(data.sessionId)
              + '&fileId=' + encodeURIComponent(file.id)
              + (pin ? '&pin=' + encodeURIComponent(pin) : '');
            const name = document.createElement('span');
            name.textContent = file.fileName;
            const size = document.createElement('span');
            size.className = 'muted';
            size.textContent = formatSize(file.size);
            link.append(name, size);
            box.append(link);
          }
        }
        start().catch(() => { document.getElementById('status').textContent = 'Error'; });
        </script>
        </body>
        </html>
        """;
}
