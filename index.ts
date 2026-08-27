import { PluginSDK } from '@logitech/plugin-sdk';
import { OpenCodexDesktopAction } from './src/actions/open-codex-desktop';
import { SwitchToChatGptAction } from './src/actions/switch-to-chatgpt';
import { SwitchToCodexAction } from './src/actions/switch-to-codex';

const pluginSDK = new PluginSDK();

pluginSDK.registerAction(new OpenCodexDesktopAction());
pluginSDK.registerAction(new SwitchToChatGptAction());
pluginSDK.registerAction(new SwitchToCodexAction());

await pluginSDK.connect();
