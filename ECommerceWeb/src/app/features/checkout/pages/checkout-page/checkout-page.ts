import { CurrencyPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';

import { STRIPE_PUBLISHABLE_KEY } from '../../../../core/config/stripe-config';
import { AddressResponseDto } from '../../../addresses/data-access/address.dto';
import { AddressesApi } from '../../../addresses/data-access/addresses-api';
import { BasketApi } from '../../../basket/data-access/basket-api';
import { BasketResponseDto } from '../../../basket/data-access/basket.dto';
import { ProductImage } from '../../../products/ui/product-image/product-image';
import { CheckoutApi } from '../../data-access/checkout-api';
import { CheckoutResponseDto } from '../../data-access/checkout.dto';
import { StripePaymentElementComponent } from '../../ui/stripe-payment-element/stripe-payment-element';

@Component({
  selector: 'app-checkout-page',
  imports: [CurrencyPipe, RouterLink, ProductImage, StripePaymentElementComponent],
  templateUrl: './checkout-page.html',
  styleUrl: './checkout-page.scss',
})
export class CheckoutPage {
  private readonly basketApi = inject(BasketApi);
  private readonly addressesApi = inject(AddressesApi);
  private readonly checkoutApi = inject(CheckoutApi);

  readonly stripePublishableKey = STRIPE_PUBLISHABLE_KEY;

  readonly basket = signal<BasketResponseDto | null>(null);

  readonly addresses = signal<AddressResponseDto[]>([]);

  readonly selectedAddressId = signal<number | null>(null);

  readonly isLoading = signal(true);

  readonly loadError = signal<string | null>(null);

  readonly isSubmitting = signal(false);

  readonly checkoutError = signal<string | null>(null);

  readonly checkout = signal<CheckoutResponseDto | null>(null);

  readonly clientSecret = computed(() => this.checkout()?.clientSecret ?? null);

  readonly selectedAddress = computed(() => {
    const id = this.selectedAddressId();

    if (id === null) {
      return null;
    }

    return this.addresses().find((address) => address.id === id) ?? null;
  });

  constructor() {
    this.loadCheckout();
  }

  selectAddress(addressId: number): void {
    if (this.checkout()) {
      return;
    }

    this.selectedAddressId.set(addressId);

    this.checkoutError.set(null);
  }

  placeOrder(): void {
    const addressId = this.selectedAddressId();

    const basket = this.basket();

    if (
      addressId === null ||
      !basket ||
      basket.items.length === 0 ||
      this.isSubmitting() ||
      this.checkout()
    ) {
      return;
    }

    this.isSubmitting.set(true);
    this.checkoutError.set(null);

    this.checkoutApi
      .checkout({
        addressId,
      })
      .subscribe({
        next: (checkout) => {
          this.checkout.set(checkout);

          this.isSubmitting.set(false);
        },

        error: (error: HttpErrorResponse) => {
          this.isSubmitting.set(false);

          this.checkoutError.set(
            this.getErrorMessage(
              error,
              'We could not start payment. Please review your basket and try again.',
            ),
          );
        },
      });
  }

  private loadCheckout(): void {
    this.isLoading.set(true);
    this.loadError.set(null);

    forkJoin({
      basket: this.basketApi.getMyBasket(),

      addresses: this.addressesApi.getMyAddresses(),
    }).subscribe({
      next: ({ basket, addresses }) => {
        this.basket.set(basket);

        this.addresses.set(addresses);

        const defaultAddress = addresses.find((address) => address.isDefault) ?? addresses[0];

        this.selectedAddressId.set(defaultAddress?.id ?? null);

        this.isLoading.set(false);
      },

      error: (error: HttpErrorResponse) => {
        this.isLoading.set(false);

        if (error.status === 401 || error.status === 403) {
          this.loadError.set('You are not authorized to access checkout.');

          return;
        }

        this.loadError.set('We could not prepare checkout. Please try again.');
      },
    });
  }

  private getErrorMessage(error: HttpErrorResponse, fallback: string): string {
    const detail = error.error?.detail;

    if (typeof detail === 'string' && detail.trim().length > 0) {
      return detail;
    }

    const message = error.error?.message;

    if (typeof message === 'string' && message.trim().length > 0) {
      return message;
    }

    return fallback;
  }
}
