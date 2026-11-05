import { useEffect, useState } from 'react';

const INITIAL_COUNT = 20;
const CHUNK_SIZE = 250;

// Shared across all seasons so only one chunk renders per task and the
// browser can paint between them, even when every season expands at once.
const queue: Array<() => void> = [];
let isScheduled = false;

function runNext() {
  isScheduled = false;
  queue.shift()?.();

  if (queue.length) {
    isScheduled = true;
    setTimeout(runNext, 0);
  }
}

function enqueue(callback: () => void) {
  queue.push(callback);

  if (!isScheduled) {
    isScheduled = true;
    setTimeout(runNext, 0);
  }

  return () => {
    const index = queue.indexOf(callback);

    if (index >= 0) {
      queue.splice(index, 1);
    }
  };
}

// Mounting thousands of rows at once blocks the page for seconds, so render
// the first rows right away and add the rest over the following frames.
function useChunkedCount(total: number, isEnabled: boolean) {
  const [count, setCount] = useState(INITIAL_COUNT);

  useEffect(() => {
    if (!isEnabled) {
      setCount(INITIAL_COUNT);
      return undefined;
    }

    if (count >= total) {
      return undefined;
    }

    return enqueue(() => {
      setCount((current) => current + CHUNK_SIZE);
    });
  }, [count, total, isEnabled]);

  return Math.min(count, total);
}

export default useChunkedCount;
