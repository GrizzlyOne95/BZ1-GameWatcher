export interface StockVehicleWeapon {
    slot: number;
    hardpoint: string | null;
    odf: string | null;
}

export interface StockVehicleDefinition {
    code: string;
    unitName: string;
    baseName: string | null;
    classLabel: string | null;
    scrapValue: number | null;
    scrapCost: number | null;
    buildTime: number | null;
    maxHealth: number | null;
    maxAmmo: number | null;
    aiName: string | null;
    aiName2: string | null;
    heatSignature: number | null;
    imageSignature: number | null;
    radarSignature: number | null;
    weaponMask: string | null;
    weapons: StockVehicleWeapon[];
}

/**
 * Generated/curated stock ODF metadata keyed by the lowercase ODF filename without `.odf`.
 *
 * Keep unknown or modded craft codes out of this table: the UI deliberately falls back to their
 * raw code rather than guessing. Use `tools/build-stock-vehicle-catalog.py` to regenerate this
 * file from an exported stock ODF directory once the complete source folder is available locally.
 */
export const STOCK_VEHICLES: Readonly<Record<string, StockVehicleDefinition>> = Object.freeze({
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
        weapons: [
            { slot: 1, hardpoint: 'GC1', odf: 'grktbomb' },
            { slot: 2, hardpoint: 'GR1', odf: 'grktbomb' },
            { slot: 3, hardpoint: 'GS1', odf: null },
            { slot: 4, hardpoint: 'GM1', odf: null }
        ]
    }
});

export function findStockVehicle(code: string | null | undefined): StockVehicleDefinition | null {
    const normalizedCode = code?.trim().toLowerCase().replace(/\.odf$/i, '');
    return normalizedCode ? STOCK_VEHICLES[normalizedCode] ?? null : null;
}
