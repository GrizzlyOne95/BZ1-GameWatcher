import { GENERATED_STOCK_VEHICLES } from './stock-vehicles.generated';

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
