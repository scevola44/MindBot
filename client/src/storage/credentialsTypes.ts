export interface Credentials {
  baseUrl: string;
  token: string;
  /** The device name the server reported when the token was checked. */
  device: string;
}
