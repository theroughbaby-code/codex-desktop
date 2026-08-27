import { PluginSDK } from '@logitech/plugin-sdk';

const pluginSDK = new PluginSDK();

// Actions are registered in Step 2 after the base plugin scaffold builds cleanly.
await pluginSDK.connect();
