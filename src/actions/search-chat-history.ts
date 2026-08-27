import { CommandAction } from '@logitech/plugin-sdk';

import { triggerChatGptShortcut } from '../codex/shortcut';

export class SearchChatHistoryAction extends CommandAction {
  name = 'search_chat_history';
  displayName = 'Search';
  description = 'Focuses ChatGPT/Codex Desktop and opens search with Ctrl+K.';
  groupName = 'Codex Desktop';

  async onKeyDown(): Promise<void> {
    await triggerChatGptShortcut('Search');
  }
}
