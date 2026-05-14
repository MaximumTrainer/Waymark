import type { JourneyAnalyticsSink, JourneyAnalyticsEvent } from './JourneyAnalyticsContext'

/**
 * Development analytics sink that logs journey events to the browser console.
 * Register this during local development for zero-config debugging.
 */
export const consoleAnalyticsSink: JourneyAnalyticsSink = {
  name: 'console',
  track(event: JourneyAnalyticsEvent): void {
    console.log('[JourneyAnalytics]', event.eventType, event)
  },
}
