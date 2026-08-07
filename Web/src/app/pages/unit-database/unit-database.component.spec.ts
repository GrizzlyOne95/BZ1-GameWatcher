import { provideHttpClient } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { UnitDatabaseComponent } from './unit-database.component';

describe('UnitDatabaseComponent', () => {
    let fixture: ComponentFixture<UnitDatabaseComponent>;
    let component: UnitDatabaseComponent;

    beforeEach(async () => {
        await TestBed.configureTestingModule({
            imports: [UnitDatabaseComponent],
            providers: [provideRouter([]), provideHttpClient()]
        }).compileComponents();
        fixture = TestBed.createComponent(UnitDatabaseComponent);
        component = fixture.componentInstance;
        fixture.detectChanges();
    });

    afterEach(() => {
        fixture.destroy();
    });

    it('loads the generated stock catalog', () => {
        expect(component.units.length).toBe(229);
        expect(component.filteredUnits.length).toBe(229);
    });

    it('finds Red Devil variants by friendly name and ODF code', () => {
        component.searchTerm = 'bvrmpa';
        expect(component.filteredUnits.some(unit => unit.code === 'bvrmpa' && unit.unitName === 'Red Devil')).toBeTrue();

        component.searchTerm = 'Red Devil';
        expect(component.filteredUnits.filter(unit => unit.unitName === 'Red Devil').length).toBeGreaterThan(1);
    });

    it('filters by faction and class', () => {
        component.selectedFaction = 'blackDog';
        component.selectedClass = 'wingman';

        expect(component.filteredUnits.length).toBeGreaterThan(0);
        expect(component.filteredUnits.every(unit => unit.faction === 'blackDog' && unit.classLabel === 'wingman')).toBeTrue();
    });
});
