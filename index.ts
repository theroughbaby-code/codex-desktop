import { PluginSDK } from '@logitech/plugin-sdk';
import { OpenCodexDesktopAction } from './src/actions/open-codex-desktop';

const pluginSDK = new PluginSDK();

pluginSDK.registerAction(new OpenCodexDesktopAction());

await pluginSDK.connect();
