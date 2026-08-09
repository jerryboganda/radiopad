import { describe, it, expect } from 'vitest';
import { navGroups, Icons } from '../components/shell/nav.config';
import enMessages from '../messages/en.json';
import deMessages from '../messages/de.json';
import esMessages from '../messages/es.json';
import frMessages from '../messages/fr.json';
import hiMessages from '../messages/hi.json';
import ptMessages from '../messages/pt.json';

describe('Navigation — Device Pairing item', () => {
  it('includes /device-pairing in the workspace group for desktop surface', () => {
    const workspaceGroup = navGroups.find((g) => g.labelKey === 'workspace');
    expect(workspaceGroup).toBeDefined();
    const item = workspaceGroup?.items.find((it) => it.href === '/device-pairing');
    expect(item).toBeDefined();
    expect(item?.labelKey).toBe('devicePairing');
    expect(item?.icon).toBe(Icons.devicePairing);
    expect(item?.permission).toBeUndefined(); // visible to all signed-in users
  });

  it('has translation for devicePairing in all locale message files', () => {
    expect((enMessages.nav as any).devicePairing).toBe('Device pairing');
    expect((deMessages.nav as any).devicePairing).toBe('Gerätekopplung');
    expect((esMessages.nav as any).devicePairing).toBe('Emparejamiento de dispositivos');
    expect((frMessages.nav as any).devicePairing).toBe("Couplage d'appareils");
    expect((hiMessages.nav as any).devicePairing).toBe('डिवाइस पेयरिंग');
    expect((ptMessages.nav as any).devicePairing).toBe('Emparelhamento de dispositivos');
  });
});
