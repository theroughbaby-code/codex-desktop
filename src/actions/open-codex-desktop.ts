import { CommandAction } from '@logitech/plugin-sdk';

import { openCodexDesktop } from '../codex/launch';

export class OpenCodexDesktopAction extends CommandAction {
  name = 'open_codex_desktop';
  displayName = 'Open Codex';
  description = 'Opens ChatGPT/Codex Desktop after locating codex.exe.';
  groupName = 'Codex Desktop';

  async onKeyDown(): Promise<void> {
    await openCodexDesktop();
  }
}
