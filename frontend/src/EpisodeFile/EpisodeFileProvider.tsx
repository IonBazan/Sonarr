import React, {
  createContext,
  PropsWithChildren,
  useContext,
  useMemo,
} from 'react';
import findById from 'Utilities/Array/findById';
import { EpisodeFile } from './EpisodeFile';
import useEpisodeFiles, { EpisodeFileFilter } from './useEpisodeFiles';

export const EpisodeFileContext = createContext<
  ReadonlyArray<EpisodeFile> | undefined
>(undefined);

export default function EpisodeFileProvider({
  children,
  ...filter
}: PropsWithChildren<EpisodeFileFilter>) {
  const { data } = useEpisodeFiles(filter);

  return (
    <EpisodeFileContext.Provider value={data}>
      {children}
    </EpisodeFileContext.Provider>
  );
}

function useEpisodeFileFromQuery(id: number | undefined) {
  const { data } = useEpisodeFiles({ episodeFileIds: id ? [id] : [] });

  return data.find((item) => item.id === id);
}

export function useEpisodeFile(id: number | undefined) {
  const episodeFiles = useContext(EpisodeFileContext);

  // A component is always either inside or outside the provider, so the same
  // hook runs on every render. Skipping the query inside the provider avoids
  // an observer per call, which adds up to tens of thousands on long series.
  const useFallback = episodeFiles ? useNoQuery : useEpisodeFileFromQuery;
  const queriedEpisodeFile = useFallback(id);

  return useMemo(
    () => (episodeFiles ? findById(episodeFiles, id) : queriedEpisodeFile),
    [episodeFiles, id, queriedEpisodeFile]
  );
}

function useNoQuery(_id: number | undefined): EpisodeFile | undefined {
  return undefined;
}

export interface SeriesEpisodeFile {
  count: number;
  episodesWithFiles: number;
}
