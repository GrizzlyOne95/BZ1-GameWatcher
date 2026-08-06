// Generated/seeded stock ODF data. Regenerate with tools/build-stock-vehicle-catalog.py.

import type { StockVehicleDefinition } from './stock-vehicles';

export const GENERATED_STOCK_VEHICLES: Readonly<Record<string, StockVehicleDefinition>> = Object.freeze({
    bvrmpa: {
        code: 'bvrmpa',
        unitName: 'Red Devil',
        baseName: 'bvrdev',
        classLabel: 'wingman',
        scrapValue: 4,
        scrapCost: 6,
        buildTime: 7,
        maxHealth: 1800,
        maxAmmo: 1750,
        aiName: 'TankFriend',
        aiName2: 'TankEnemy',
        heatSignature: 1.6,
        imageSignature: 2.7,
        radarSignature: 0.3,
        weaponMask: '00001',
        thumbnailUrl: null,
        weapons: [
            { slot: 1, hardpoint: 'GC1', odf: 'grktbomb' },
            { slot: 2, hardpoint: 'GR1', odf: 'grktbomb' },
            { slot: 3, hardpoint: 'GS1', odf: null },
            { slot: 4, hardpoint: 'GM1', odf: null }
        ]
    }
});
