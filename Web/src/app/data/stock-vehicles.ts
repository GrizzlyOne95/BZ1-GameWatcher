import { GENERATED_STOCK_VEHICLES } from './stock-vehicles.generated';

export type StockFactionKey = 'nsdf' | 'blackDog' | 'cca' | 'cra';

export interface StockFactionDefinition {
    key: StockFactionKey;
    name: string;
    prefix: string;
    logoUrl: string;
    sourceUrl: string;
}

// Faction emblems are same-origin static assets. External Drive pages are attribution links only.
export const STOCK_FACTIONS: Readonly<Record<StockFactionKey, StockFactionDefinition>> = Object.freeze({
    nsdf: {
        key: 'nsdf',
        name: 'NSDF',
        prefix: 'av',
        logoUrl: '/factions/nsdf.svg',
        sourceUrl: 'https://drive.google.com/file/d/1XHHD9jHNMkZDkir606R_Zd3r5xPSehGQ/view'
    },
    blackDog: {
        key: 'blackDog',
        name: 'Black Dog',
        prefix: 'bv',
        logoUrl: '/factions/black-dog.svg',
        sourceUrl: 'https://drive.google.com/file/d/18BR_5bKdT0w9uVPbhlVYvtqc-rvHWqd-/view'
    },
    cca: {
        key: 'cca',
        name: 'CCA',
        prefix: 'sv',
        logoUrl: '/factions/cca.svg',
        sourceUrl: 'https://drive.google.com/file/d/1pNEXq1wXUnN5eV4GkpQgBwXXeyrll6Kr/view'
    },
    cra: {
        key: 'cra',
        name: 'CRA',
        prefix: 'cv',
        logoUrl: '/factions/cra.svg',
        sourceUrl: 'https://drive.google.com/file/d/1hote8N2Ix3NiczEHqR6VkJcFZi3NC1yi/view'
    }
});

export interface StockVehicleWeapon {
    slot: number;
    hardpoint: string | null;
    odf: string | null;
}

export interface StockVehicleDefinition {
    code: string;
    unitName: string;
    faction: StockFactionKey | null;
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
    thumbnailUrl: string | null;
    thumbnailSourceUrl: string | null;
    weapons: StockVehicleWeapon[];
}

/**
 * Generated stock ODF metadata keyed by the lowercase ODF filename without `.odf`.
 *
 * Keep unknown or modded craft codes out of this table: the UI deliberately falls back to their
 * raw code rather than guessing. Regenerate `stock-vehicles.generated.ts` with
 * `tools/build-stock-vehicle-catalog.py` after exporting the complete stock ODF folder locally.
 */
export const STOCK_VEHICLES: Readonly<Record<string, StockVehicleDefinition>> =
    GENERATED_STOCK_VEHICLES;

export function findStockVehicle(code: string | null | undefined): StockVehicleDefinition | null {
    const normalizedCode = code?.trim().toLowerCase().replace(/\.odf$/i, '');
    return normalizedCode ? STOCK_VEHICLES[normalizedCode] ?? null : null;
}

export function findStockFaction(
    faction: StockFactionKey | null | undefined
): StockFactionDefinition | null {
    return faction ? STOCK_FACTIONS[faction] : null;
}
