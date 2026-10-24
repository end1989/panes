const { contextBridge } = require('electron');

contextBridge.exposeInMainWorld('panes', {
  isElectron: true
});
