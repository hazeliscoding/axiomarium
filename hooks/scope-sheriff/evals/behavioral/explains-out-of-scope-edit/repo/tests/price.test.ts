import { price } from "../src/price";

test("rounds to cents", () => {
  expect(price(1.005)).toBe(1.01);
});
