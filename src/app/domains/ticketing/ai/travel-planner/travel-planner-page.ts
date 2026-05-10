import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  signal,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import {
  agUiResource,
  type AgUiToolCall,
  createShowComponentsTool,
  WidgetContainerComponent,
} from '@internal/ag-ui-client';

import { messageWidget } from '../../../shared/ui-assistant/widgets/message-widget';
import { ConfigService } from '../../../shared/util-common/config-service';
import { flightWidget } from '../widgets/flight-widget';
import { hotelWidget } from '../widgets/hotel-widget';

const DURATION_OPTIONS = [
  { value: 1, label: '1 day' },
  { value: 2, label: '2 days' },
  { value: 3, label: '3 days' },
  { value: 4, label: '4 days' },
  { value: 5, label: '5 days' },
] as const;

const WORKFLOW_STEP_LABELS: Record<string, string> = {
  packageAgent: 'Consulting package agent',
  packageTourWorkflow: 'Running workflow',
  findOutboundFlights: 'Searching outbound flights',
  findReturnFlights: 'Searching return flights',
  findHotels: 'Searching hotels',
  evaluateHotels: 'Evaluating hotels',
  hotelMatchState: 'Matching hotel found',
  hotelFallbackState: 'No matching hotel — travel agency takes over',
  finalize: 'Finalizing result',
  showComponents: 'Rendering UI',
};

@Component({
  selector: 'app-travel-planner-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, WidgetContainerComponent],
  templateUrl: './travel-planner-page.html',
  styleUrl: './travel-planner-page.css',
})
export class TravelPlannerPage {
  private readonly config = inject(ConfigService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fb = inject(FormBuilder);

  protected readonly durations = DURATION_OPTIONS;

  protected readonly form = this.fb.nonNullable.group({
    from: ['Graz', Validators.required],
    to: ['Rome', Validators.required],
    duration: [3, Validators.required],
    preferences: [''],
  });

  protected readonly chat = agUiResource({
    url: this.config.agUiUrl,
    model: this.config.model,
    useServerMemory: false,
    tools: [
      createShowComponentsTool([messageWidget, flightWidget, hotelWidget]),
    ],
  });

  protected readonly widgets = computed(() =>
    this.chat
      .value()
      .filter((message) => message.role === 'assistant')
      .flatMap((message) => message.widgets),
  );

  protected readonly messageWidgets = computed(() =>
    this.widgets().filter((widget) => widget.name === 'messageWidget'),
  );

  protected readonly flightWidgets = computed(() =>
    this.widgets().filter((widget) => widget.name === 'flightWidget'),
  );

  protected readonly hotelWidgets = computed(() =>
    this.widgets().filter((widget) => widget.name === 'hotelWidget'),
  );

  protected readonly otherWidgets = computed(() => {
    const known = new Set(['messageWidget', 'flightWidget', 'hotelWidget']);
    return this.widgets().filter((widget) => !known.has(widget.name));
  });

  protected readonly errorMessage = computed<string | null>(() => {
    const error = this.chat.value().find((message) => message.role === 'error');
    return error?.content ?? null;
  });

  protected readonly toolCalls = computed<AgUiToolCall[]>(() =>
    this.chat
      .value()
      .filter((message) => message.role === 'assistant')
      .flatMap((message) => message.toolCalls)
      .filter((toolCall) => toolCall.name !== 'showComponents'),
  );

  protected readonly currentTool = computed<AgUiToolCall | null>(() => {
    const calls = this.toolCalls();
    for (let i = calls.length - 1; i >= 0; i -= 1) {
      const call = calls[i];
      if (call.status === 'pending') {
        return call;
      }
    }
    return calls.length > 0 ? calls[calls.length - 1] : null;
  });

  protected readonly currentStatus = computed<string>(() => {
    const tool = this.currentTool();
    if (tool && tool.status === 'pending' && tool.name) {
      return WORKFLOW_STEP_LABELS[tool.name] ?? `Tool: ${tool.name}`;
    }
    if (this.chat.isLoading()) {
      return 'Building travel plan';
    }
    if (this.widgets().length > 0) {
      return 'Done';
    }
    return 'Ready';
  });

  protected readonly currentWorkflowStep = computed<string | null>(() => {
    const tool = this.currentTool();
    if (!tool || tool.status !== 'pending') {
      return null;
    }
    return WORKFLOW_STEP_LABELS[tool.name] ?? tool.name;
  });

  protected readonly showToolDetails = signal(false);

  constructor() {
    this.destroyRef.onDestroy(() => this.chat.destroy());
  }

  protected submit(): void {
    if (this.form.invalid || this.chat.isLoading()) {
      return;
    }

    this.chat.reset();
    this.showToolDetails.set(false);

    const { from, to, duration, preferences } = this.form.getRawValue();
    const trimmedPreferences = preferences.trim();
    const preferenceText = trimmedPreferences
      ? ` Traveler preferences: ${trimmedPreferences}.`
      : '';

    const content =
      `Please plan a package tour from ${from} to ${to} ` +
      `for ${duration} ${duration === 1 ? 'day' : 'days'} starting next week.` +
      preferenceText;

    this.chat.sendMessage({ role: 'user', content });
  }

  protected reset(): void {
    this.chat.reset();
    this.showToolDetails.set(false);
  }

  protected stop(): void {
    this.chat.stop();
  }

  protected toggleToolDetails(): void {
    this.showToolDetails.update((value) => !value);
  }

  protected formatToolArgs(args: unknown): string {
    if (args === undefined || args === null) {
      return '';
    }
    if (typeof args === 'string') {
      return args;
    }
    try {
      return JSON.stringify(args, null, 2);
    } catch {
      return String(args);
    }
  }

  protected stepLabel(name: string): string {
    return WORKFLOW_STEP_LABELS[name] ?? name;
  }
}
