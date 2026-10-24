const { app, BrowserWindow, session, Menu, shell } = require('electron');
const path = require('path');
const windowStateKeeper = require('electron-window-state');

// Single instance lock — focus existing window if already running
const gotTheLock = app.requestSingleInstanceLock();
if (!gotTheLock) {
  app.quit();
}

let mainWindow;

function createWindow() {
  const mainWindowState = windowStateKeeper({
    defaultWidth: 1920,
    defaultHeight: 1080
  });

  mainWindow = new BrowserWindow({
    x: mainWindowState.x,
    y: mainWindowState.y,
    width: mainWindowState.width,
    height: mainWindowState.height,
    minWidth: 640,
    minHeight: 480,
    backgroundColor: '#000000',
    icon: path.join(__dirname, 'build', 'icon.ico'),
    title: 'Panes',
    show: false,
    autoHideMenuBar: true,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      nodeIntegration: false,
      contextIsolation: true,
      webviewTag: false,
      webSecurity: true,
      allowRunningInsecureContent: false
    }
  });

  mainWindowState.manage(mainWindow);

  mainWindow.once('ready-to-show', () => {
    mainWindow.show();
  });

  // Strip CSP and X-Frame-Options headers from video embed domains
  // so iframes load correctly from the file:// origin
  session.defaultSession.webRequest.onHeadersReceived((details, callback) => {
    const responseHeaders = { ...details.responseHeaders };
    const url = details.url.toLowerCase();

    const embedDomains = [
      'twitch.tv',
      'player.twitch.tv',
      'youtube.com',
      'youtube-nocookie.com',
      'player.vimeo.com',
      'vimeo.com',
      'dailymotion.com',
      'www.dailymotion.com',
      'player.kick.com',
      'kick.com'
    ];

    if (embedDomains.some(domain => url.includes(domain))) {
      delete responseHeaders['x-frame-options'];
      delete responseHeaders['X-Frame-Options'];

      const cspKeys = Object.keys(responseHeaders).filter(
        k => k.toLowerCase() === 'content-security-policy'
      );
      cspKeys.forEach(key => {
        if (responseHeaders[key]) {
          responseHeaders[key] = responseHeaders[key].map(policy =>
            policy.replace(/frame-ancestors[^;]*(;|$)/gi, 'frame-ancestors * ;')
          );
        }
      });
    }

    callback({ responseHeaders });
  });

  mainWindow.loadFile('index.html');

  // Open external links in default browser
  mainWindow.webContents.setWindowOpenHandler(({ url }) => {
    shell.openExternal(url);
    return { action: 'deny' };
  });

  // F11 for native Electron fullscreen
  mainWindow.webContents.on('before-input-event', (event, input) => {
    if (input.key === 'F11' && input.type === 'keyDown') {
      mainWindow.setFullScreen(!mainWindow.isFullScreen());
      event.preventDefault();
    }
  });

  mainWindow.on('closed', () => {
    mainWindow = null;
  });
}

function createMenu() {
  const template = [
    {
      label: 'File',
      submenu: [
        { role: 'quit' }
      ]
    },
    {
      label: 'View',
      submenu: [
        { role: 'reload' },
        { role: 'forceReload' },
        { role: 'toggleDevTools' },
        { type: 'separator' },
        { role: 'resetZoom' },
        { role: 'zoomIn' },
        { role: 'zoomOut' },
        { type: 'separator' },
        { role: 'togglefullscreen' }
      ]
    }
  ];
  Menu.setApplicationMenu(Menu.buildFromTemplate(template));
}

app.whenReady().then(() => {
  createMenu();
  createWindow();
});

app.on('second-instance', () => {
  if (mainWindow) {
    if (mainWindow.isMinimized()) mainWindow.restore();
    mainWindow.focus();
  }
});

app.on('window-all-closed', () => {
  app.quit();
});
