import { OrderResponseDto } from '../../orders/data-access/order.dto';

export interface CheckoutRequestDto {
  addressId: number;
}

export interface CheckoutResponseDto {
  order: OrderResponseDto;
  clientSecret: string;
}
