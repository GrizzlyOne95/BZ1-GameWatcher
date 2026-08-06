import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { SiteNavComponent } from '../../components/site-nav/site-nav.component';
import {
    STOCK_FACTIONS,
    STOCK_VEHICLES,
    StockFactionDefinition,
    StockFactionKey,
    StockVehicleDefinition,
    findStockFaction
} from '../../data/stock-vehicles';

type FactionFilter = StockFactionKey | 'all';
type UnitSort = 'name' | 'faction' | 'health' | 'cost';

@Component({
    selector: 'app-unit-database',
    imports: [CommonModule, FormsModule, SiteNavComponent],
    templateUrl: './unit-database.component.html',
    styleUrl: './unit-database.page.scss'
})
export class UnitDatabaseComponent {
    readonly factions = Object.values(STOCK_FACTIONS);
    readonly units = Object.values(STOCK_VEHICLES);
    readonly classLabels = [...new Set(this.units.map(unit => unit.classLabel).filter((value): value is string => Boolean(value)))]
        .sort((left, right) => this.readableClass(left).localeCompare(this.readableClass(right)));

    searchTerm = '';
    selectedFaction: FactionFilter = 'all';
    selectedClass = 'all';
    sortBy: UnitSort = 'name';
    onlyWithImages = false;

    get filteredUnits(): StockVehicleDefinition[] {
        const query = this.searchTerm.trim().toLowerCase();
        const filtered = this.units.filter(unit => {
            if (this.selectedFaction !== 'all' && unit.faction !== this.selectedFaction) {
                return false;
            }

            if (this.selectedClass !== 'all' && unit.classLabel !== this.selectedClass) {
                return false;
            }

            if (this.onlyWithImages && !unit.thumbnailUrl) {
                return false;
            }

            if (!query) {
                return true;
            }

            const searchable = [
                unit.unitName,
                unit.code,
                unit.baseName,
                unit.classLabel,
                unit.aiName,
                unit.aiName2,
                this.factionFor(unit)?.name,
                ...unit.weapons.flatMap(weapon => [weapon.hardpoint, weapon.odf])
            ]
                .filter((value): value is string => Boolean(value))
                .join(' ')
                .toLowerCase();

            return searchable.includes(query);
        });

        return filtered.sort((left, right) => {
            switch (this.sortBy) {
                case 'faction':
                    return this.factionName(left).localeCompare(this.factionName(right))
                        || left.unitName.localeCompare(right.unitName)
                        || left.code.localeCompare(right.code);
                case 'health':
                    return (right.maxHealth ?? -1) - (left.maxHealth ?? -1)
                        || left.unitName.localeCompare(right.unitName);
                case 'cost':
                    return (right.scrapCost ?? -1) - (left.scrapCost ?? -1)
                        || left.unitName.localeCompare(right.unitName);
                default:
                    return left.unitName.localeCompare(right.unitName)
                        || left.code.localeCompare(right.code);
            }
        });
    }

    factionFor(unit: StockVehicleDefinition): StockFactionDefinition | null {
        return findStockFaction(unit.faction);
    }

    factionName(unit: StockVehicleDefinition): string {
        return this.factionFor(unit)?.name ?? 'Unknown';
    }

    readableClass(value: string | null | undefined): string {
        if (!value) {
            return 'Unclassified';
        }

        return value
            .replace(/([a-z])([A-Z])/g, '$1 $2')
            .replace(/[_-]+/g, ' ')
            .replace(/\b\w/g, character => character.toUpperCase());
    }

    display(value: string | number | null | undefined, suffix = ''): string {
        return value === null || value === undefined || value === '' ? '—' : `${value}${suffix}`;
    }

    trackUnit(_index: number, unit: StockVehicleDefinition): string {
        return unit.code;
    }

    clearFilters(): void {
        this.searchTerm = '';
        this.selectedFaction = 'all';
        this.selectedClass = 'all';
        this.sortBy = 'name';
        this.onlyWithImages = false;
    }

    hideBrokenImage(event: Event): void {
        const image = event.currentTarget as HTMLImageElement | null;
        if (image) {
            image.hidden = true;
        }
    }
}
