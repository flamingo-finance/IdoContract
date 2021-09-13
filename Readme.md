# Flamingo IDO Contract

Flamingo IDO is Flamingo’s Initial DEX Offering, providing a platform for launching and investing in new tokens.

[Read more about the IDO here](https://docs.flamingo.finance/)

## Development

The IDO platform consist of a main IDO contract and several "IDO pair"-contracts. 
The main IDO contract is responsible for most of the platform logic while the individual
"IDO pair"-contracts represents individual token offerings and is responsible for most
of the token offerings asset management.

The main IDO contract can be found in the `contracts/IdoContract` directory.

An example contract for an "IDO pair"-contract can be found in the `contracts/IdoPairContract` directory.

### Testing

**Note:** To test the main IDO contract it is first necessary to deploy some other contracts on 
the private test chain. We refer to these contract as mock-contracts, an they can be found in 
the `contracts/IdoContract/mock` directory.

To run the unit tests for the IDO contract run the following command:
```
cd contracts/IdoContract/test
dotnet test
```

### Build

To build the main IDO contract run the following command:
```
cd contracts/IdoContract/src
dotnet build
```
