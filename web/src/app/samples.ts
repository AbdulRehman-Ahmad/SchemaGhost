export interface Sample { id: string; name: string; description: string; legacy: object; modern: object }

export const SAMPLES: Sample[] = [
  {
    id: 'default', name: 'Full migration (default)',
    description: 'Renames, nesting, a string-to-boolean change and a moved number, all in one response.',
    legacy: { user_id: 1024, name: 'Ali', email: 'ali@example.com', is_active: true, balance: 250.75 },
    modern: { account: { id: 1024, status: 'active' }, full_name: 'Ali', contact: { email_address: 'ali@example.com' }, isActive: 'true', wallet: { balance: 250.75 } },
  },
  {
    id: 'rename', name: 'Field renaming',
    description: 'Simple renames. The easy case.',
    legacy: { product_name: 'Laptop', price: 999.5, in_stock: 14 },
    modern: { name: 'Laptop', price: 999.5, stock: 14 },
  },
  {
    id: 'nested', name: 'Nested JSON changes',
    description: 'Flat fields moved into nested objects.',
    legacy: { city: 'Lahore', zip: '54000', lat: 31.5204, lng: 74.3587 },
    modern: { address: { city: 'Lahore', postal_code: '54000' }, location: { latitude: 31.5204, longitude: 74.3587 } },
  },
  {
    id: 'types', name: 'Data type changes',
    description: 'Same data, different types. Safe conversions are offered; guessing is not.',
    legacy: { id: 1024, count: 7, active: true },
    modern: { id: '1024', count: '7', active: 'true' },
  },
  {
    id: 'removed', name: 'Removed field',
    description: 'The new API no longer returns the phone number. SchemaGhost must not invent it.',
    legacy: { email: 'a@x.com', phone: '555-0100' },
    modern: { contact: { email: 'a@x.com' } },
  },
  {
    id: 'ambiguous', name: 'Ambiguous mapping',
    description: 'Two fields hold the same value. Value matching cannot tell which is which.',
    legacy: { low: 10, high: 10 },
    modern: { min: 10, max: 10 },
  },
  {
    id: 'unsafe', name: 'Unsafe transformation',
    description: 'Try mapping is_active from status with toBool. Verification will reject it: "active" is not a boolean.',
    legacy: { id: 1, is_active: true, balance: 250.75 },
    modern: { id: 1, status: 'active', wallet: { balance: 250.75 } },
  },
];
