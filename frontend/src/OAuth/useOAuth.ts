import { useCallback, useRef, useState } from 'react';
import { type Error } from 'App/State/AppSectionState';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import requestAction from 'Utilities/requestAction';

const callbackPath = `${window.Sonarr.urlBase}/oauth.html`;
const callbackUrl = `${window.location.origin}${callbackPath}`;

interface OAuthResult {
  [key: string]: string | number | boolean;
}

interface OAuthState {
  authorizing: boolean;
  result: OAuthResult | null;
  error: Error | null;
}

interface StartOAuthParams {
  name: string;
  provider?: string;
  providerData?: Record<string, unknown>;
  [key: string]: unknown;
}

interface OAuthResponse {
  oauthUrl?: string;
  poll?: boolean;
  success?: boolean;
  [key: string]: unknown;
}

interface QueryParams {
  [key: string]: string;
}

const POLL_INTERVAL = 5000;
const MAX_POLLS = 60;
const CLOSED_CHECK_INTERVAL = 500;

function isCancelled(error: unknown) {
  return error instanceof DOMException && error.name === 'AbortError';
}

function showOAuthWindow(
  url: string,
  payload: StartOAuthParams,
  signal: AbortSignal,
  poll = false,
  ajaxOptions?: Record<string, unknown>
): Promise<QueryParams> {
  return new Promise((resolve, reject) => {
    // Opened blank first so the provider page gets no opener and can't navigate this tab
    const newWindow = window.open('', '_blank');

    if (
      !newWindow ||
      newWindow.closed ||
      typeof newWindow.closed === 'undefined'
    ) {
      // A fake validation error to mimic a 400 response from the API.
      const error = Object.assign(
        new Error('Pop-ups are being blocked by your browser'),
        {
          status: 400,
          responseJSON: [
            {
              propertyName: payload.name,
              errorMessage: 'Pop-ups are being blocked by your browser',
            },
          ],
        }
      );

      return reject(error);
    }

    newWindow.opener = null;
    newWindow.location.href = url;

    const channel = new BroadcastChannel(`oauth:${callbackPath}`);
    let pollTimeout: ReturnType<typeof setTimeout> | undefined = undefined;
    let closedInterval: ReturnType<typeof setInterval> | undefined = undefined;

    const finish = (settle: () => void) => {
      clearTimeout(pollTimeout);
      clearInterval(closedInterval);
      channel.close();
      signal.removeEventListener('abort', handleAbort);
      settle();
    };

    const handleAbort = () => {
      finish(() => reject(new DOMException('OAuth cancelled', 'AbortError')));
    };

    signal.addEventListener('abort', handleAbort);

    if (poll) {
      let polls = 0;

      const pollAction = () => {
        polls++;

        requestAction({
          action: 'pollOAuth',
          queryParams: ajaxOptions,
          ...payload,
        }).then(
          (response: OAuthResponse) => {
            if (signal.aborted) {
              return;
            }

            if (response.success) {
              finish(() => resolve({}));
            } else if (polls >= MAX_POLLS) {
              finish(() =>
                reject(new Error('Timed out waiting for authorization'))
              );
            } else {
              pollTimeout = setTimeout(pollAction, POLL_INTERVAL);
            }
          },
          (error: unknown) => {
            finish(() => reject(error));
          }
        );
      };

      pollTimeout = setTimeout(pollAction, POLL_INTERVAL);
    } else {
      channel.onmessage = (event: MessageEvent<string>) => {
        finish(() => {
          newWindow.close();
          resolve(Object.fromEntries(new URLSearchParams(event.data)));
        });
      };

      closedInterval = setInterval(() => {
        if (newWindow.closed) {
          handleAbort();
        }
      }, CLOSED_CHECK_INTERVAL);
    }
  });
}

function executeIntermediateRequest(
  payload: Record<string, unknown>,
  ajaxOptions: Record<string, unknown>
): Promise<OAuthResponse> {
  return createAjaxRequest(ajaxOptions).request.then(
    (data: Record<string, unknown>) => {
      return requestAction({
        action: 'continueOAuth',
        queryParams: {
          ...data,
          callbackUrl,
        },
        ...payload,
      });
    }
  );
}

const useOAuth = () => {
  const [oAuthState, setOAuthState] = useState<OAuthState>({
    authorizing: false,
    result: null,
    error: null,
  });

  const setOAuthValue = useCallback((values: Partial<OAuthState>) => {
    setOAuthState((prev) => ({ ...prev, ...values }));
  }, []);

  const abortController = useRef<AbortController | null>(null);

  const resetOAuth = useCallback(() => {
    abortController.current?.abort();
    abortController.current = null;

    setOAuthState({
      authorizing: false,
      result: null,
      error: null,
    });
  }, []);

  const startOAuth = useCallback(
    async (params: StartOAuthParams) => {
      const { name, ...otherPayload } = params;

      const actionPayload = {
        action: 'startOAuth',
        queryParams: { callbackUrl },
        ...otherPayload,
      };

      abortController.current?.abort();

      const controller = new AbortController();
      abortController.current = controller;

      setOAuthValue({ authorizing: true });

      try {
        let startResponse: OAuthResponse = {};

        const response = (await requestAction(actionPayload)) as OAuthResponse;
        startResponse = response;

        let queryParams: QueryParams | null = null;

        if (response.oauthUrl) {
          queryParams = await showOAuthWindow(
            response.oauthUrl,
            params,
            controller.signal
          );
        } else {
          const intermediateResponse = await executeIntermediateRequest(
            otherPayload,
            response // Pass the entire response as ajaxOptions
          );
          startResponse = intermediateResponse;

          if (!intermediateResponse.oauthUrl) {
            throw new Error('No OAuth URL received from intermediate request');
          }

          queryParams = await showOAuthWindow(
            intermediateResponse.oauthUrl,
            params,
            controller.signal,
            intermediateResponse.poll || false,
            intermediateResponse
          );
        }

        const tokenResponse = await requestAction({
          action: 'getOAuthToken',
          queryParams: {
            ...startResponse,
            ...queryParams,
          },
          ...otherPayload,
        });

        setOAuthValue({
          authorizing: false,
          result: tokenResponse,
          error: null,
        });

        return tokenResponse;
      } catch (error) {
        if (isCancelled(error)) {
          setOAuthValue({ authorizing: false });

          return null;
        }

        const oAuthError = error as Error;
        setOAuthValue({
          authorizing: false,
          result: null,
          error: oAuthError,
        });

        throw error;
      }
    },
    [setOAuthValue]
  );

  return {
    ...oAuthState,
    startOAuth,
    setOAuthValue,
    resetOAuth,
  };
};

export default useOAuth;
