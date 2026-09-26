import {
  AfterViewInit,
  Component,
  ElementRef,
  Input,
  OnDestroy,
  ViewChild,
  signal,
} from '@angular/core';

import { loadStripe, Stripe, StripeElements, StripePaymentElement } from '@stripe/stripe-js';

@Component({
  selector: 'app-stripe-payment-element',
  templateUrl: './stripe-payment-element.html',
  styleUrl: './stripe-payment-element.scss',
})
export class StripePaymentElementComponent implements AfterViewInit, OnDestroy {
  @Input({ required: true })
  clientSecret!: string;

  @Input({ required: true })
  publishableKey!: string;

  @ViewChild('paymentElement')
  private paymentElementContainer!: ElementRef<HTMLDivElement>;

  readonly isLoading = signal(true);
  readonly isPaying = signal(false);

  readonly errorMessage = signal<string | null>(null);

  readonly successMessage = signal<string | null>(null);

  private stripe: Stripe | null = null;
  private elements: StripeElements | null = null;

  private paymentElement: StripePaymentElement | null = null;

  async ngAfterViewInit(): Promise<void> {
    try {
      this.stripe = await loadStripe(this.publishableKey);

      if (!this.stripe) {
        this.errorMessage.set('Stripe could not be loaded.');

        this.isLoading.set(false);

        return;
      }

      this.elements = this.stripe.elements({
        clientSecret: this.clientSecret,
      });

      this.paymentElement = this.elements.create('payment', {
        layout: 'tabs',
      });

      this.paymentElement.on('ready', () => {
        this.isLoading.set(false);
      });

      this.paymentElement.mount(this.paymentElementContainer.nativeElement);
    } catch (error) {
      console.error('Stripe initialization error:', error);

      this.errorMessage.set('The payment form could not be loaded.');

      this.isLoading.set(false);
    }
  }

  async pay(): Promise<void> {
    if (!this.stripe || !this.elements || this.isPaying()) {
      return;
    }

    this.isPaying.set(true);
    this.errorMessage.set(null);
    this.successMessage.set(null);

    try {
      const submitResult = await this.elements.submit();

      if (submitResult.error) {
        this.errorMessage.set(submitResult.error.message ?? 'Please check your payment details.');

        return;
      }

      const result = await this.stripe.confirmPayment({
        elements: this.elements,

        confirmParams: {
          return_url: `${window.location.origin}/checkout?payment=return`,
        },

        redirect: 'if_required',
      });

      if (result.error) {
        this.errorMessage.set(result.error.message ?? 'Payment could not be completed.');

        return;
      }

      const paymentIntent = result.paymentIntent;

      if (!paymentIntent) {
        this.errorMessage.set('Stripe did not return a payment result.');

        return;
      }

      if (paymentIntent.status === 'succeeded') {
        this.successMessage.set(
          'Payment was accepted by Stripe. Backend confirmation is the next step.',
        );

        return;
      }

      if (paymentIntent.status === 'processing') {
        this.successMessage.set('Your payment is processing.');

        return;
      }

      this.successMessage.set(`Payment status: ${paymentIntent.status}`);
    } catch (error) {
      console.error('Stripe payment error:', error);

      this.errorMessage.set(
        'An unexpected payment error occurred. Check the browser console for details.',
      );
    } finally {
      this.isPaying.set(false);
    }
  }

  ngOnDestroy(): void {
    this.paymentElement?.unmount();
  }
}
