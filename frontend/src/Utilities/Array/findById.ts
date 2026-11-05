interface Item {
  id: number;
}

// Rows look up items in the same shared array, so index it once instead of scanning per row.
const indexes = new WeakMap<ReadonlyArray<Item>, Map<number, Item>>();

export default function findById<T extends Item>(
  items: ReadonlyArray<T> | undefined,
  id: number | undefined
): T | undefined {
  if (!items || id === undefined) {
    return undefined;
  }

  let index = indexes.get(items);

  if (!index) {
    index = new Map(items.map((item) => [item.id, item]));
    indexes.set(items, index);
  }

  return index.get(id) as T | undefined;
}
