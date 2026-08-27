import { PluginSDK } from '@logitech/plugin-sdk';
import { OpenBuiltInBrowserAction } from './src/actions/open-built-in-browser';
import { OpenCompanionWindowAction } from './src/actions/open-companion-window';
import { OpenCodexDesktopAction } from './src/actions/open-codex-desktop';
import { SearchChatHistoryAction } from './src/actions/search-chat-history';

const pluginSDK = new PluginSDK();

pluginSDK.registerAction(new OpenCodexDesktopAction());
pluginSDK.registerAction(new OpenCompanionWindowAction());
pluginSDK.registerAction(new SearchChatHistoryAction());
pluginSDK.registerAction(new OpenBuiltInBrowserAction());

await pluginSDK.connect();
