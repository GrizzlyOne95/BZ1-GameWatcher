import { Observable, distinctUntilChanged, fromEvent, map, startWith, switchMap, timer } from 'rxjs';

/**
 * Poll immediately/regularly while the page is visible, but dramatically reduce background-tab
 * traffic. Returning to the tab starts a fresh visible timer, so live pages refresh right away.
 */
export function visibilityAwareTimer(
    activeIntervalMs: number,
    hiddenIntervalMs = Math.max(60_000, activeIntervalMs),
    visibleInitialDelayMs = 0): Observable<number> {
    return fromEvent(document, 'visibilitychange')
        .pipe(
            startWith(null),
            map(() => document.visibilityState === 'hidden'),
            distinctUntilChanged(),
            switchMap(hidden => timer(
                hidden ? hiddenIntervalMs : visibleInitialDelayMs,
                hidden ? hiddenIntervalMs : activeIntervalMs
            ))
        );
}
